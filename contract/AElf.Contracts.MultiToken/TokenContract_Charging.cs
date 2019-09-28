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
        public override Empty ChargeTransactionFees(ChargeTransactionFeesInput input)
        {
            Assert(input.MethodName != null && input.ContractAddress != null && input.TransactionSize > 0,
                "Invalid charge transaction fees input.");

            State.MethodFeeProviderContract.Value = input.ContractAddress;

            var fee = input.ContractAddress == Context.Self
                ? GetMethodFee(input.MethodName)
                : State.MethodFeeProviderContract.GetMethodFee.Call(new StringValue {Value = input.MethodName});

            if (fee == null || !fee.Fee.Any()) return new Empty();

            if (!ChargeFirstSufficientToken(fee.Fee.ToDictionary(f => f.Symbol, f => f.BasicFee), out var symbol,
                out var amount, out var existingBalance))
            {
                Assert(false, "Failed to charge first sufficient token.");
            }

            var bill = new TransactionFeeBill
            {
                TokenToAmount =
                {
                    {Context.Variables.NativeSymbol, amount}
                }
            };

            var fromAddress = Context.Sender;
            State.Balances[fromAddress][symbol] = existingBalance.Sub(amount);
            var oldBill = State.ChargedFees[fromAddress];
            State.ChargedFees[fromAddress] = oldBill == null ? bill : oldBill + bill;
            return new Empty();
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

        private bool ChargeFirstSufficientToken(Dictionary<string, long> symbolToAmountMap, out string symbol,
            out long amount, out long existingBalance)
        {
            symbol = null;
            amount = 0L;
            existingBalance = 0L;
            var fromAddress = Context.Sender;

            // Traverse available token symbols, check balance one by one
            // until there's balance of one certain token is enough to pay the fee.
            foreach (var symbolToAmount in symbolToAmountMap)
            {
                existingBalance = State.Balances[fromAddress][symbolToAmount.Key];
                symbol = symbolToAmount.Key;
                amount = symbolToAmount.Value;

                Assert(amount > 0, $"Invalid transaction fee amount of token {symbolToAmount.Key}.");

                if (existingBalance >= amount)
                {
                    break;
                }
            }

            // Traversed all available tokens, can't find balance of any token enough to pay transaction fee.
            Assert(existingBalance >= amount, "Insufficient balance to pay transaction fee.");

            return symbol != null;
        }

        public override Empty ClaimTransactionFees(Empty input)
        {
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

            return new Empty();
        }

        /// <summary>
        /// Burn 10%
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="totalAmount"></param>
        private void TransferTransactionFeesToFeeReceiver(string symbol, long totalAmount)
        {
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

        public override Empty DonateResourceToken(Empty input)
        {
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
    }
}