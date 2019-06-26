using System.Collections.Generic;
using System.Linq;
using AElf.Contracts.MultiToken.Messages;
using AElf.Sdk.CSharp;
using AElf.Types;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContract
    {
        private static bool IsValidSymbolChar(char character)
        {
            return character >= 'A' && character <= 'Z';
        }

        private TokenInfo AssertValidToken(string symbol, long amount)
        {
            Assert(!string.IsNullOrEmpty(symbol) & symbol.All(IsValidSymbolChar),
                "Invalid symbol.");
            Assert(amount > 0, "Invalid amount.");
            var tokenInfo = State.TokenInfos[symbol];
            Assert(tokenInfo != null && tokenInfo != new TokenInfo(), "Token is not found.");
            Assert(!tokenInfo.IsTransferDisabled, "Token can't transfer.");
            return tokenInfo;
        }
        
        private void DoTransfer(Address from, Address to, string symbol, long amount, string memo)
        {
            Assert(from != to, "Can't do transfer to sender itself.");
            var balanceOfSender = State.Balances[from][symbol];
            Assert(balanceOfSender >= amount, $"Insufficient balance. {symbol}: {balanceOfSender} / {amount}");
            var balanceOfReceiver = State.Balances[to][symbol];
            State.Balances[from][symbol] = balanceOfSender.Sub(amount);
            State.Balances[to][symbol] = balanceOfReceiver.Add(amount);
            Context.Fire(new Transferred()
            {
                From = from,
                To = to,
                Symbol = symbol,
                Amount = amount,
                Memo = memo
            });
        }

        private void AssertLockAddress(string symbol, Address address)
        {
            var symbolState = State.LockWhiteLists[symbol];
            Assert(symbolState != null && symbolState[address], "Not in white list.");
            // TODO: Why is this added in a big commit: https://github.com/AElfProject/AElf/commit/b8d67cc78bf0ffae623585cf8e52f966fdf00d09#r33581502
            // Assert(symbolState != null && symbolState[Context.Sender], "Not in white list.");
        }

        private void RegisterTokenInfo(TokenInfo tokenInfo)
        {
            Assert(!string.IsNullOrEmpty(tokenInfo.Symbol) & tokenInfo.Symbol.All(IsValidSymbolChar),
                "Invalid symbol.");
            Assert(!string.IsNullOrEmpty(tokenInfo.TokenName), "Invalid token name.");
            Assert(tokenInfo.TotalSupply > 0, "Invalid total supply.");
            Assert(tokenInfo.Issuer != null, "Invalid issuer address.");
            State.TokenInfos[tokenInfo.Symbol] = tokenInfo;
        }
    }
}