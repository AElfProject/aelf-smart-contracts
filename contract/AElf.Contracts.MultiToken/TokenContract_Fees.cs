using System.Collections.Generic;
using System.Linq;
using Acs1;
using AElf.Contracts.Treasury;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContract
    {
        /// <summary>
        /// Related transactions will be generated by acs1 pre-plugin service,
        /// and will be executed before the origin transaction.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override TransactionFee ChargeTransactionFees(ChargeTransactionFeesInput input)
        {
            Assert(input.MethodName != null && input.ContractAddress != null, "Invalid charge transaction fees input.");

            var result = new TransactionFee();

            if (input.PrimaryTokenSymbol == string.Empty)
            {
                // Primary token not created yet.
                return result;
            }

            var fee = Context.Call<MethodFees>(input.ContractAddress, nameof(GetMethodFee),
                new StringValue {Value = input.MethodName});

            var bill = new TransactionFeeBill();

            var fromAddress = Context.Sender;

            string symbol = null;
            long existingBalance = 0;
            if (fee != null && fee.Fees.Any())
            {
                if (!ChargeFirstSufficientToken(fee.Fees.ToDictionary(f => f.Symbol, f => f.BasicFee), out symbol,
                    out var amount, out existingBalance))
                {
                    Context.LogDebug(() => "Failed to charge first sufficient token.");
                    var txFee = new TransactionFee
                    {
                        IsFailedToCharge = true
                    };
                    if (symbol != null)
                    {
                        State.Balances[fromAddress][symbol] = 0;
                        bill.TokenToAmount.Add(symbol, existingBalance);
                        var feeBill = State.ChargedFees[fromAddress];
                        State.ChargedFees[fromAddress] = feeBill == null ? bill : feeBill + bill;
                        txFee.Value.Add(symbol, existingBalance);
                    }
                    // If symbol == null, then charge nothing.
                    return txFee;
                }

                bill.TokenToAmount.Add(symbol, amount);
                // Charge base fee.
                State.Balances[fromAddress][symbol] = existingBalance.Sub(amount);
                result.Value.Add(symbol, amount);
            }

            var primaryTokenBalanceAfterChargingBaseFee = State.Balances[fromAddress][input.PrimaryTokenSymbol];
            var txSizeFeeAmount = input.TransactionSizeFee;
            txSizeFeeAmount = primaryTokenBalanceAfterChargingBaseFee > txSizeFeeAmount // Enough to pay tx size fee.
                ? txSizeFeeAmount
                // It's safe to convert from long to int here because
                // balanceAfterChargingBaseFee <= txSizeFeeAmount and
                // typeof(txSizeFeeAmount) == int
                : (int) primaryTokenBalanceAfterChargingBaseFee;

            bill += new TransactionFeeBill
            {
                TokenToAmount =
                {
                    {
                        input.PrimaryTokenSymbol,
                        txSizeFeeAmount
                    }
                }
            };

            // Charge tx size fee.
            var finalBalanceOfNativeSymbol = primaryTokenBalanceAfterChargingBaseFee.Sub(txSizeFeeAmount);
            State.Balances[fromAddress][input.PrimaryTokenSymbol] = finalBalanceOfNativeSymbol;

            // Record the bill finally.
            var oldBill = State.ChargedFees[fromAddress];
            State.ChargedFees[fromAddress] = oldBill == null ? bill : oldBill + bill;

            // If balanceAfterChargingBaseFee < txSizeFeeAmount, make sender's balance of native symbol to 0 and make current execution failed.
            if (primaryTokenBalanceAfterChargingBaseFee < input.TransactionSizeFee)
            {
                Context.LogDebug(() =>
                    $"Insufficient balance to pay tx size fee: {primaryTokenBalanceAfterChargingBaseFee} < {input.TransactionSizeFee}.\n " +
                    $"Primary token: {input.PrimaryTokenSymbol}");
                var txFee = new TransactionFee
                {
                    IsFailedToCharge = true
                };
                if (symbol != null)
                {
                    State.Balances[fromAddress][symbol] = 0;
                    State.Balances[fromAddress][input.PrimaryTokenSymbol] = 0;
                    bill.TokenToAmount.Add(symbol, existingBalance);
                    bill.TokenToAmount.Add(input.PrimaryTokenSymbol, primaryTokenBalanceAfterChargingBaseFee);
                    var feeBill = State.ChargedFees[fromAddress];
                    State.ChargedFees[fromAddress] = feeBill == null ? bill : feeBill + bill;
                    txFee.Value.Add(symbol, existingBalance);
                    txFee.Value.Add(input.PrimaryTokenSymbol, primaryTokenBalanceAfterChargingBaseFee);
                }
 
                return new TransactionFee {IsFailedToCharge = true};
            }

            if (result.Value.ContainsKey(input.PrimaryTokenSymbol))
            {
                result.Value[input.PrimaryTokenSymbol] =
                    result.Value[input.PrimaryTokenSymbol].Add(txSizeFeeAmount);
            }
            else
            {
                result.Value.Add(input.PrimaryTokenSymbol, txSizeFeeAmount);
            }

            return result;
        }

        public override Empty ChargeResourceToken(ChargeResourceTokenInput input)
        {
            Context.LogDebug(() => $"Start executing ChargeResourceToken.{input}");
            if (input.Equals(new ChargeResourceTokenInput()))
            {
                return new Empty();
            }

            var symbolToAmount = new Dictionary<string, long>
            {
                {"CPU", State.CpuUnitPrice.Value.Mul(input.ReadsCount)},
                {"NET", State.NetUnitPrice.Value.Mul(input.TransactionSize)},
                {"STO", State.StoUnitPrice.Value.Mul(input.WritesCount)}
            };
            foreach (var pair in symbolToAmount)
            {
                Context.LogDebug(() => $"Charging {pair.Value} {pair.Key} tokens.");
                var existingBalance = State.Balances[Context.Sender][pair.Key];
                Assert(existingBalance >= pair.Value,
                    $"Insufficient resource. {pair.Key}: {existingBalance} / {pair.Value}");
                State.ChargedResourceTokens[input.Caller][Context.Sender][pair.Key] =
                    State.ChargedResourceTokens[input.Caller][Context.Sender][pair.Key].Add(pair.Value);
            }

            Context.LogDebug(() => "Finished executing ChargeResourceToken.");

            return new Empty();
        }

        /// <summary>
        /// Example 1:
        /// symbolToAmountMap: {{"ELF", 10}, {"TSA", 1}, {"TSB", 2}}
        ///
        /// [Charge successful]
        /// Sender's balance:
        /// ELF - 9
        /// TSA - 0
        /// TSB - 3
        /// Then charge 2 TSBs.
        ///
        /// [Charge failed]
        /// Sender's balance:
        /// ELF - 9
        /// TSA - 0
        /// TSB - 1
        /// Then charge 9 ELFs
        ///
        /// Example 2:
        /// symbolToAmountMap: {{"TSA", 1}, {"TSB", 2}}
        /// which means the charging token symbol list doesn't contain the native symbol.
        ///
        /// [Charge successful]
        /// Sender's balance:
        /// ELF - 1
        /// TSA - 2
        /// TSB - 2
        /// Then charge 1 TSA
        ///
        /// [Charge failed]
        /// Sender's balance:
        /// ELF - 1
        /// TSA - 0
        /// TSB - 1
        /// Then charge 1 TSB
        ///
        /// [Charge failed]
        /// Sender's balance:
        /// ELF - 1000000000
        /// TSA - 0
        /// TSB - 0
        /// Then charge nothing.
        /// (Contract developer should be suggested to implement acs5 to check certain balance or allowance of sender.)
        /// </summary>
        /// <param name="symbolToAmountMap"></param>
        /// <param name="symbol"></param>
        /// <param name="amount"></param>
        /// <param name="existingBalance"></param>
        /// <returns></returns>
        private bool ChargeFirstSufficientToken(Dictionary<string, long> symbolToAmountMap, out string symbol,
            out long amount, out long existingBalance)
        {
            symbol = null;
            amount = 0L;
            existingBalance = 0L;
            var fromAddress = Context.Sender;
            string symbolOfValidBalance = null;

            // Traverse available token symbols, check balance one by one
            // until there's balance of one certain token is enough to pay the fee.
            foreach (var symbolToAmount in symbolToAmountMap)
            {
                existingBalance = State.Balances[fromAddress][symbolToAmount.Key];
                symbol = symbolToAmount.Key;
                amount = symbolToAmount.Value;

                if (existingBalance > 0)
                {
                    symbolOfValidBalance = symbol;
                }

                if (existingBalance >= amount) break;
            }

            if (existingBalance >= amount) return true;

            var officialTokenContractAddress =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            var primaryTokenSymbol =
                Context.Call<StringValue>(officialTokenContractAddress, nameof(GetPrimaryTokenSymbol), new Empty())
                    .Value;
            symbol = symbolToAmountMap.Keys.Contains(primaryTokenSymbol) ? primaryTokenSymbol : symbolOfValidBalance;

            return false;
        }

        public override Empty ClaimTransactionFees(Empty input)
        {
            Context.LogDebug(() => "Claim transaction fee.");
            if (State.TreasuryContract.Value == null)
            {
                var treasuryContractAddress =
                    Context.GetContractAddressByName(SmartContractConstants.TreasuryContractSystemName);
                if (treasuryContractAddress == null)
                {
                    // Which means Treasury Contract didn't deployed yet. Ignore this method.
                    return new Empty();
                }

                State.TreasuryContract.Value = treasuryContractAddress;
            }

            var transactions = Context.GetPreviousBlockTransactions();
            Context.LogDebug(() => $"Got {transactions.Count} transaction(s) from previous block.");

            var senders = transactions.Select(t => t.From).ToList();
            var totalBill = new TransactionFeeBill();
            foreach (var sender in senders)
            {
                var oldBill = State.ChargedFees[sender];
                if (oldBill != null)
                {
                    totalBill += oldBill;
                }

                // Clear
                State.ChargedFees[sender] = new TransactionFeeBill();
            }

            foreach (var bill in totalBill.TokenToAmount)
            {
                var symbol = bill.Key;
                var amount = bill.Value;
                State.Balances[Context.Self][symbol] = State.Balances[Context.Self][symbol].Add(amount);
                TransferTransactionFeesToFeeReceiver(symbol, amount);
            }

            Context.LogDebug(() => "Finish claim transaction fee.");

            return new Empty();
        }

        public override Empty DonateResourceToken(Empty input)
        {
            Context.LogDebug(() => "Start donate resource token.");

            var isMainChain = true;
            if (State.TreasuryContract.Value == null)
            {
                var treasuryContractAddress =
                    Context.GetContractAddressByName(SmartContractConstants.TreasuryContractSystemName);
                if (treasuryContractAddress == null)
                {
                    isMainChain = false;
                }
                else
                {
                    State.TreasuryContract.Value = treasuryContractAddress;
                }
            }

            var transactions = Context.GetPreviousBlockTransactions();

            Context.LogDebug(() => $"Got {transactions.Count} transaction(s) from previous block.");
            foreach (var symbol in Context.Variables.ResourceTokenSymbolNameList.Except(new List<string> {"RAM"}))
            {
                var totalAmount = 0L;
                foreach (var transaction in transactions)
                {
                    var caller = transaction.From;
                    var contractAddress = transaction.To;
                    var amount = State.ChargedResourceTokens[caller][contractAddress][symbol];
                    if (amount > 0)
                    {
                        State.Balances[contractAddress][symbol] = State.Balances[contractAddress][symbol].Sub(amount);
                        Context.LogDebug(() => $"Charged {amount} {symbol} tokens from {contractAddress}");
                        totalAmount = totalAmount.Add(amount);
                        State.ChargedResourceTokens[caller][contractAddress][symbol] = 0;
                    }
                }

                Context.LogDebug(() => $"Charged resource token {symbol}: {totalAmount}");

                if (totalAmount > 0)
                {
                    if (isMainChain)
                    {
                        Context.LogDebug(() => $"Adding {totalAmount} of {symbol}s to dividend pool.");
                        // Main Chain.
                        State.Balances[Context.Self][symbol] = State.Balances[Context.Self][symbol].Add(totalAmount);
                        State.TreasuryContract.Donate.Send(new DonateInput
                        {
                            Symbol = symbol,
                            Amount = totalAmount
                        });
                    }
                    else
                    {
                        Context.LogDebug(() => $"Adding {totalAmount} of {symbol}s to consensus address account.");
                        // Side Chain
                        var consensusContractAddress =
                            Context.GetContractAddressByName(SmartContractConstants.ConsensusContractSystemName);
                        State.Balances[consensusContractAddress][symbol] =
                            State.Balances[consensusContractAddress][symbol].Add(totalAmount);
                    }
                }
            }

            return new Empty();
        }

        /// <summary>
        /// Burn 10%
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="totalAmount"></param>
        private void TransferTransactionFeesToFeeReceiver(string symbol, long totalAmount)
        {
            Context.LogDebug(() => "Transfer transaction fee to receiver.");

            if (totalAmount <= 0) return;

            var burnAmount = totalAmount.Div(10);
            Context.SendInline(Context.Self, nameof(Burn), new BurnInput
            {
                Symbol = symbol,
                Amount = burnAmount
            });

            var transferAmount = totalAmount.Sub(burnAmount);
            if (State.TreasuryContract.Value != null)
            {
                // Main chain would donate tx fees to dividend pool.
                State.TreasuryContract.Donate.Send(new DonateInput
                {
                    Symbol = symbol,
                    Amount = transferAmount
                });
            }
            else
            {
                if (State.FeeReceiver.Value != null)
                {
                    Transfer(new TransferInput
                    {
                        To = State.FeeReceiver.Value,
                        Symbol = symbol,
                        Amount = transferAmount,
                    });
                }
                else
                {
                    // Burn all!
                    Burn(new BurnInput
                    {
                        Symbol = symbol,
                        Amount = transferAmount
                    });
                }
            }
        }

        public override Empty SetFeeReceiver(Address input)
        {
            Assert(State.FeeReceiver.Value != null, "Fee receiver already set.");
            State.FeeReceiver.Value = input;
            return new Empty();
        }

        public override Empty SetTransactionSizeUnitPrice(SInt64Value input)
        {
            if (State.ZeroContract.Value == null)
            {
                State.ZeroContract.Value = Context.GetZeroSmartContractAddress();
            }

            if (State.ParliamentAuthContract.Value == null)
            {
                State.ParliamentAuthContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ParliamentAuthContractSystemName);
            }

            var contractOwner = State.ZeroContract.GetContractAuthor.Call(Context.Self);

            Assert(
                contractOwner == Context.Sender ||
                Context.Sender == State.ParliamentAuthContract.GetGenesisOwnerAddress.Call(new Empty()) ||
                Context.Sender == Context.GetContractAddressByName(SmartContractConstants.EconomicContractSystemName),
                "No permission to set tx size unit price.");

            Context.Fire(new TransactionSizeFeeUnitPriceUpdated
            {
                UnitPrice = input.Value
            });

            Context.LogDebug(() => $"SetTransactionSizeUnitPrice: {input.Value}");

            State.TransactionFeeUnitPrice.Value = input.Value;

            return new Empty();
        }
    }
}