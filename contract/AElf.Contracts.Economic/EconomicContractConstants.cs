namespace AElf.Contracts.Economic
{
    public static class EconomicContractConstants
    {
        public const long NativeTokenConnectorInitialVirtualBalance = 100_000_00000000;

        // Token Converter Contract related.
        public const string TokenConverterFeeRate = "0.005";
        public const string TokenConverterTokenSymbol = "AETC";
        public const long TokenConverterTokenTotalSupply = 1_000_000_000_00000000;
        public const int TokenConverterTokenDecimals = 8;
        public const long TokenConverterTokenConnectorInitialVirtualBalance = 100_000_00000000;

        // Resource token related.
        public const long ResourceTokenTotalSupply = 500_000_000_00000000;

        public const int ResourceTokenDecimals = 8;

        //resource to sell
        public const long ResourceTokenInitialVirtualBalance = 100_000;

        public const string NativeTokenPrefix = "nt";

        public const long NativeTokenToResourceBalance = 10_000_000_00000000;

        // Election related.
        public const string ElectionTokenSymbol = "VOTE";
        public const string ShareTokenSymbol = "SHARE";
        public const long ElectionTokenTotalSupply = long.MaxValue;

        public const int MemoMaxLength = 64;
        
        public const string PayTxFeeSymbolListName = "SymbolListToPayTxFee";
        public const string PayRentalSymbolListName = "SymbolListToPayRental";
    }
}