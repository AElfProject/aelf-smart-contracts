using System;
using System.Collections.Generic;
using System.Linq;
using Acs0;
using AElf.Contracts.CrossChain;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.TokenConverter;
using AElf.Contracts.Treasury;
using AElf.CSharp.Core;
using AElf.Kernel;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Approved = AElf.Contracts.MultiToken.Messages.Approved;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContract : TokenContractImplContainer.TokenContractImplBase
    {
        public override Empty Create(CreateInput input)
        {
            var existing = State.TokenInfos[input.Symbol];
            Assert(existing == null || !existing.Symbol.Any(), $"Token already exists. Symbol: {input.Symbol}");
            RegisterTokenInfo(new TokenInfo
            {
                Symbol = input.Symbol,
                TokenName = input.TokenName,
                TotalSupply = input.TotalSupply,
                Decimals = input.Decimals,
                Issuer = input.Issuer,
                IsBurnable = input.IsBurnable,
                IsTransferDisabled = input.IsTransferDisabled
            });

            if (string.IsNullOrEmpty(State.NativeTokenSymbol.Value))
            {
                State.NativeTokenSymbol.Value = input.Symbol;
            }

            foreach (var address in input.LockWhiteList)
            {
                State.LockWhiteLists[input.Symbol][address] = true;
            }

            return new Empty();
        }

        public override Empty Issue(IssueInput input)
        {
            Assert(input.To != null, "To address not filled.");
            var tokenInfo = AssertValidToken(input.Symbol, input.Amount);
            Assert(tokenInfo.Issuer == Context.Sender || Context.Sender == Context.GetZeroSmartContractAddress(),
                $"Sender is not allowed to issue token {input.Symbol}.");
            tokenInfo.Supply = tokenInfo.Supply.Add(input.Amount);
            Assert(tokenInfo.Supply <= tokenInfo.TotalSupply, "Total supply exceeded");
            State.TokenInfos[input.Symbol] = tokenInfo;
            State.Balances[input.To][input.Symbol] = input.Amount;
            return new Empty();
        }

        public override Empty Transfer(TransferInput input)
        {
            AssertValidToken(input.Symbol, input.Amount);
            DoTransfer(Context.Sender, input.To, input.Symbol, input.Amount, input.Memo);
            return new Empty();
        }

        public override Empty CrossChainTransfer(CrossChainTransferInput input)
        {
            AssertValidToken(input.TokenInfo.Symbol, input.Amount);
            var burnInput = new BurnInput
            {
                Amount = input.Amount,
                Symbol = input.TokenInfo.Symbol
            };
            Burn(burnInput);
            return new Empty();
        }

        public override Empty CrossChainReceiveToken(CrossChainReceiveTokenInput input)
        {
            var transferTransaction = Transaction.Parser.ParseFrom(input.TransferTransactionBytes);
            var transferTransactionHash = transferTransaction.GetHash();

            Context.LogDebug(() => $"transferTransactionHash == {transferTransactionHash}");
            Assert(State.VerifiedCrossChainTransferTransaction[transferTransactionHash] == null,
                "Token already claimed.");

            var crossChainTransferInput =
                CrossChainTransferInput.Parser.ParseFrom(transferTransaction.Params.ToByteArray());
            var symbol = crossChainTransferInput.TokenInfo.Symbol;
            var amount = crossChainTransferInput.Amount;
            var receivingAddress = crossChainTransferInput.To;
            var targetChainId = crossChainTransferInput.ToChainId;
            var transferSender = transferTransaction.From;
            Context.LogDebug(() =>
                $"symbol == {symbol}, amount == {amount}, receivingAddress == {receivingAddress}, targetChainId == {targetChainId}");

            Assert(transferSender.Equals(Context.Sender) && targetChainId == Context.ChainId,
                "Unable to claim cross chain token.");
            if (State.CrossChainContract.Value == null)
                State.CrossChainContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.CrossChainContractSystemName);
            var verificationInput = new VerifyTransactionInput
            {
                TransactionId = transferTransactionHash,
                ParentChainHeight = input.ParentChainHeight,
                VerifiedChainId = input.FromChainId
            };
            verificationInput.Path.AddRange(input.MerklePath);
            if (State.CrossChainContract.Value == null)
                State.CrossChainContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.CrossChainContractSystemName);
            var verificationResult =
                State.CrossChainContract.VerifyTransaction.Call(verificationInput);
            Assert(verificationResult.Value, "Verification failed.");

            // Create token if it doesnt exist.
            var existing = State.TokenInfos[symbol];
            if (existing == null)
                RegisterTokenInfo(crossChainTransferInput.TokenInfo);

            State.VerifiedCrossChainTransferTransaction[transferTransactionHash] = input;
            var balanceOfReceiver = State.Balances[receivingAddress][symbol];
            State.Balances[receivingAddress][symbol] = balanceOfReceiver.Add(amount);
            return new Empty();
        }

        public override Empty Lock(LockInput input)
        {
            AssertLockAddress(input.Symbol, input.To);
            AssertValidToken(input.Symbol, input.Amount);
            var fromVirtualAddress = Hash.FromRawBytes(Context.Sender.Value.Concat(input.LockId.Value).ToArray());
            var virtualAddress = Context.ConvertVirtualAddressToContractAddress(fromVirtualAddress);
            // Transfer token to virtual address.
            DoTransfer(input.From, virtualAddress, input.Symbol, input.Amount, input.Usage);
            return new Empty();
        }

        public override Empty Unlock(UnlockInput input)
        {
            AssertLockAddress(input.Symbol, input.To);
            AssertValidToken(input.Symbol, input.Amount);
            var fromVirtualAddress = Hash.FromRawBytes(Context.Sender.Value.Concat(input.LockId.Value).ToArray());
            Context.SendVirtualInline(fromVirtualAddress, Context.Self, nameof(Transfer), new TransferInput
            {
                To = input.From,
                Symbol = input.Symbol,
                Amount = input.Amount,
                Memo = input.Usage,
            });
            return new Empty();
        }

        public override Empty TransferFrom(TransferFromInput input)
        {
            AssertValidToken(input.Symbol, input.Amount);

            // First check allowance.
            var allowance = State.Allowances[input.From][Context.Sender][input.Symbol];
            if (allowance < input.Amount)
            {
                if (IsInWhiteList(new IsInWhiteListInput {Symbol = input.Symbol, Address = Context.Sender}).Value)
                {
                    DoTransfer(input.From, input.To, input.Symbol, input.Amount, input.Memo);
                    return new Empty();
                }

                Assert(false, $"Insufficient allowance. Token: {input.Symbol}; {allowance}/{input.Amount}");
            }

            DoTransfer(input.From, input.To, input.Symbol, input.Amount, input.Memo);
            State.Allowances[input.From][Context.Sender][input.Symbol] = allowance.Sub(input.Amount);
            return new Empty();
        }

        public override Empty Approve(ApproveInput input)
        {
            AssertValidToken(input.Symbol, input.Amount);
            State.Allowances[Context.Sender][input.Spender][input.Symbol] =
                State.Allowances[Context.Sender][input.Spender][input.Symbol].Add(input.Amount);
            Context.Fire(new Approved()
            {
                Owner = Context.Sender,
                Spender = input.Spender,
                Symbol = input.Symbol,
                Amount = input.Amount
            });
            return new Empty();
        }

        public override Empty UnApprove(UnApproveInput input)
        {
            AssertValidToken(input.Symbol, input.Amount);
            var oldAllowance = State.Allowances[Context.Sender][input.Spender][input.Symbol];
            var amountOrAll = Math.Min(input.Amount, oldAllowance);
            State.Allowances[Context.Sender][input.Spender][input.Symbol] = oldAllowance.Sub(amountOrAll);
            Context.Fire(new UnApproved()
            {
                Owner = Context.Sender,
                Spender = input.Spender,
                Symbol = input.Symbol,
                Amount = amountOrAll
            });
            return new Empty();
        }

        public override Empty Burn(BurnInput input)
        {
            var tokenInfo = AssertValidToken(input.Symbol, input.Amount);
            Assert(tokenInfo.IsBurnable, "The token is not burnable.");
            var existingBalance = State.Balances[Context.Sender][input.Symbol];
            Assert(existingBalance >= input.Amount, "Burner doesn't own enough balance.");
            State.Balances[Context.Sender][input.Symbol] = existingBalance.Sub(input.Amount);
            tokenInfo.Supply = tokenInfo.Supply.Sub(input.Amount);
            Context.Fire(new Burned
            {
                Burner = Context.Sender,
                Symbol = input.Symbol,
                Amount = input.Amount
            });
            return new Empty();
        }

        public override Empty ChargeTransactionFees(ChargeTransactionFeesInput input)
        {
            if (input.Equals(new ChargeTransactionFeesInput()))
            {
                return new Empty();
            }

            ChargeFirstSufficientToken(input.SymbolToAmount, out var symbol,
                out var amount, out var existingBalance);

            if (State.PreviousBlockTransactionFeeTokenSymbolList.Value == null)
            {
                State.PreviousBlockTransactionFeeTokenSymbolList.Value = new TokenSymbolList();
            }

            if (!State.PreviousBlockTransactionFeeTokenSymbolList.Value.SymbolList.Contains(symbol))
            {
                State.PreviousBlockTransactionFeeTokenSymbolList.Value.SymbolList.Add(symbol);
            }

            var fromAddress = Context.Sender;
            State.Balances[fromAddress][symbol] = existingBalance.Sub(amount);
            State.ChargedFees[fromAddress][symbol] = State.ChargedFees[fromAddress][symbol].Add(amount);
            return new Empty();
        }

        public override Empty ChargeResourceToken(ChargeResourceTokenInput input)
        {
            if (input.Equals(new ChargeResourceTokenInput()))
            {
                return new Empty();
            }

            foreach (var symbolToAmount in input.SymbolToAmount)
            {
                var existingBalance = State.Balances[Context.Sender][symbolToAmount.Key];
                Assert(existingBalance >= symbolToAmount.Value,
                    $"Insufficient resource. {symbolToAmount.Key}: {existingBalance} / {symbolToAmount.Value}");
                State.Balances[Context.Sender][symbolToAmount.Key] = existingBalance.Sub(symbolToAmount.Value);
                State.Balances[Context.Self][symbolToAmount.Key] =
                    State.Balances[Context.Self][symbolToAmount.Key].Add(symbolToAmount.Value);
                State.ChangedResources[Context.Sender][symbolToAmount.Key] =
                    State.ChangedResources[Context.Sender][symbolToAmount.Key].Add(symbolToAmount.Value);
            }
            return new Empty();
        }

        private void ChargeFirstSufficientToken(MapField<string, long> symbolToAmountMap, out string symbol,
            out long amount, out long existingBalance)
        {
            symbol = Context.Variables.NativeSymbol;
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
                if (existingBalance >= amount)
                {
                    break;
                }
            }

            // Traversed all available tokens, can't find balance of any token enough to pay transaction fee.
            Assert(existingBalance >= amount, "Insufficient balance to pay.");
        }

        public override Empty ClaimTransactionFees(Empty input)
        {
            if (State.TreasuryContract.Value == null)
            {
                State.TreasuryContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ProfitContractSystemName);
            }

            if (State.PreviousBlockTransactionFeeTokenSymbolList.Value == null ||
                !State.PreviousBlockTransactionFeeTokenSymbolList.Value.SymbolList.Any())
            {
                return new Empty();
            }

            var transactions = Context.GetPreviousBlockTransactions();
            var senders = transactions.Select(t => t.From).ToList();
            foreach (var symbol in State.PreviousBlockTransactionFeeTokenSymbolList.Value.SymbolList)
            {
                var totalFee = 0L;
                foreach (var sender in senders)
                {
                    totalFee = totalFee.Add(State.ChargedFees[sender][symbol]);
                    State.ChargedFees[sender][symbol] = 0;
                }

                State.TreasuryContract.Donate.Send(new DonateInput
                {
                    Symbol = symbol,
                    Amount = totalFee
                });
            }

            State.PreviousBlockTransactionFeeTokenSymbolList.Value = new TokenSymbolList();

            return new Empty();
        }

        public override Empty CheckThreshold(CheckThresholdInput input)
        {
            var meetThreshold = false;
            foreach (var symbolToThreshold in input.SymbolToThreshold)
            {
                if (State.Balances[input.Sender][symbolToThreshold.Key] < symbolToThreshold.Value) continue;
                meetThreshold = true;
                break;
            }

            if (input.SymbolToThreshold.Count == 0)
            {
                meetThreshold = true;
            }

            Assert(meetThreshold, "Cannot meet the calling threshold.");
            return new Empty();
        }

        public override Empty SetProfitReceivingInformation(ProfitReceivingInformation input)
        {
            if (State.ACS0Contract.Value == null)
            {
                State.ACS0Contract.Value = Context.GetZeroSmartContractAddress();
            }

            var contractOwner = State.ACS0Contract.GetContractOwner.Call(input.ContractAddress);
            Assert(contractOwner == Context.Sender || input.ContractAddress == Context.Sender,
                "Either contract owner or contract itself can set profit receiving information.");

            Assert(0 < input.DonationPartsPerHundred && input.DonationPartsPerHundred < 100, "Invalid donation ratio.");

            State.ProfitReceivingInfos[input.ContractAddress] = input;
            return new Empty();
        }

        public override Empty ReceiveProfits(ReceiveProfitsInput input)
        {
            var profitReceivingInformation = State.ProfitReceivingInfos[input.ContractAddress];
            Assert(profitReceivingInformation.ProfitReceiverAddress == Context.Sender,
                "Only profit receiver can perform this action.");
            foreach (var symbol in input.Symbols.Except(TokenContractConstants.ResourceTokenSymbols))
            {

                var profits = State.Balances[input.ContractAddress][symbol];
                State.Balances[input.ContractAddress][symbol] = 0;
                var donates = profits.Mul(profitReceivingInformation.DonationPartsPerHundred).Div(100);
                State.TreasuryContract.Donate.Send(new DonateInput
                {
                    Symbol = symbol,
                    Amount = donates
                });
                State.Balances[profitReceivingInformation.ProfitReceiverAddress][symbol] = profits.Sub(donates);
            }

            // Sell received token resources from callings of corresponding contract.
            foreach (var resourceSymbol in TokenContractConstants.ResourceTokenSymbols)
            {
                if (State.TokenConverterContract.Value == null)
                {
                    State.TokenConverterContract.Value =
                        Context.GetContractAddressByName(SmartContractConstants.TokenConverterContractSystemName);
                }

                State.TokenConverterContract.SellWithInlineAction.Send(new SellWithInlineActionInput
                {
                    Symbol = resourceSymbol,
                    Amount = State.ChangedResources[input.ContractAddress][resourceSymbol],
                    ContractAddress = Context.Self,
                    MethodName = nameof(ReturnTax),
                    Params = new ReturnTaxInput
                    {
                        BalanceBeforeSelling = State.Balances[Context.Self][Context.Variables.NativeSymbol],
                        ReturnTaxReceiverAddress = profitReceivingInformation.ProfitReceiverAddress
                    }.ToByteString()
                });

                State.ChangedResources[input.ContractAddress][resourceSymbol] = 0;
            }

            return new Empty();
        }

        /// <summary>
        /// Transfer from Context.Origin to Context.Sender.
        /// Used for contract developers to receive / share profits.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty TransferToContract(TransferToContractInput input)
        {
            AssertValidToken(input.Symbol, input.Amount);

            // First check allowance.
            var allowance = State.Allowances[Context.Origin][Context.Sender][input.Symbol];
            if (allowance < input.Amount)
            {
                if (IsInWhiteList(new IsInWhiteListInput {Symbol = input.Symbol, Address = Context.Sender}).Value)
                {
                    DoTransfer(Context.Origin, Context.Sender, input.Symbol, input.Amount, input.Memo);
                    return new Empty();
                }

                Assert(false, $"Insufficient allowance. Token: {input.Symbol}; {allowance}/{input.Amount}");
            }

            DoTransfer(Context.Origin, Context.Sender, input.Symbol, input.Amount, input.Memo);
            State.Allowances[Context.Origin][Context.Sender][input.Symbol] = allowance.Sub(input.Amount);
            return new Empty();
        }

        public override Empty ReturnTax(ReturnTaxInput input)
        {
            Assert(Context.Sender ==
                   Context.GetContractAddressByName(SmartContractConstants.TokenConverterContractSystemName),
                "Only token converter contract can return tax.");
            var amount = State.Balances[Context.Self][Context.Variables.NativeSymbol].Sub(input.BalanceBeforeSelling);
            State.Balances[input.ReturnTaxReceiverAddress][Context.Variables.NativeSymbol] =
                State.Balances[input.ReturnTaxReceiverAddress][Context.Variables.NativeSymbol].Add(amount);
            return new Empty();
        }
    }
}