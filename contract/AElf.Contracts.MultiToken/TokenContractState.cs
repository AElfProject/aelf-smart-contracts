using Acs0;
using AElf.Contracts.Association;
using AElf.Contracts.CrossChain;
using AElf.Contracts.Parliament;
using AElf.Contracts.Referendum;
using AElf.Contracts.Treasury;
using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContractState : ContractState
    {
        public StringState NativeTokenSymbol { get; set; }

        public StringState ChainPrimaryTokenSymbol { get; set; }
        public MappedState<string, TokenInfo> TokenInfos { get; set; }
        public MappedState<Address, string, long> Balances { get; set; }
        public MappedState<Address, Address, string, long> Allowances { get; set; }
        public MappedState<Address, TransactionFeeBill> ChargedFees { get; set; }

        public SingletonState<Address> FeeReceiver { get; set; }

        public MappedState<Address, Address, string, long> ChargedResourceTokens { get; set; }

        /// <summary>
        /// Contract Address -> Advance Address -> Resource Token Symbol -> Amount.
        /// </summary>
        public MappedState<Address, Address, string, long> AdvancedResourceToken { get; set; }

        /// <summary>
        /// Contract Address -> (Owning) Resource Token Symbol -> Amount.
        /// </summary>
        public MappedState<Address, string, long> OwningResourceToken { get; set; }
        public BoolState IsContractInitialized { get; set; }

        public MappedState<Address, ProfitReceivingInformation> ProfitReceivingInfos { get; set; }
        public SingletonState<Address> Owner { get; set; }
        public SingletonState<Address> NormalOrganizationForToken { get; set; }
        public SingletonState<Address> ParliamentOrganizationForCoefficient { get; set; }
        public SingletonState<Address> ParliamentOrganizationForExtraToken { get; set; }
        public SingletonState<Address> ReferendumOrganizationForCoefficient { get; set; }
        public SingletonState<Address> AssociationOrganizationForToken { get; set; }
        public SingletonState<Address> AssociationOrganizationForCoefficient { get; set; }
        public SingletonState<Address> DefaultProposer { get; set; }
        
        /// <summary>
        /// symbol -> address -> is in white list.
        /// </summary>
        public MappedState<string, Address, bool> LockWhiteLists { get; set; }
        
        public MappedState<int, Address> CrossChainTransferWhiteList { get; set; }

        public MappedState<Hash, CrossChainReceiveTokenInput> VerifiedCrossChainTransferTransaction { get; set; }
        public SingletonState<AllAvailableTokenInfo> ExtraAvailableTokenInfos { get; set; }
        public MappedState<string, Hash> ProposalMap { get; set; }
        internal CrossChainContractContainer.CrossChainContractReferenceState CrossChainContract { get; set; }

        internal TreasuryContractContainer.TreasuryContractReferenceState TreasuryContract { get; set; }

        internal ParliamentContractContainer.ParliamentContractReferenceState ParliamentContract { get; set; }

        internal ACS0Container.ACS0ReferenceState ZeroContract { get; set; }
        internal AssociationContractContainer.AssociationContractReferenceState AssociationContract { get; set; }
        internal ReferendumContractContainer.ReferendumContractReferenceState ReferendumContract { get; set; }
    }
}