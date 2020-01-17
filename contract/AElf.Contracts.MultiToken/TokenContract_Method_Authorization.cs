using System;
using System.Collections.Generic;
using Acs3;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContract
    {
        #region orgnanization init

        public override Empty InitializeAuthorizedOrganization(Empty input)
        {
            if (State.ParliamentContract.Value == null)
            {
                State.ParliamentContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ParliamentContractSystemName);
            }

            if (State.AssociationContract.Value == null)
            {
                State.AssociationContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.AssociationContractSystemName);
            }

            if (State.ReferendumContract.Value == null)
            {
                State.ReferendumContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ReferendumContractSystemName);
            }
            if(ControllersInitialized())
                return new Empty();
            CalculateUserFeeController();
            CreateReferendumOrganizationForUserFee();
            CreateAssociationOrganizationForUserFee();
            
            CalculateDeveloperFeeController();
            CreateDeveloperOrganization();
            CreateAssociationOrganizationForDeveloperFee();
            return new Empty();
        }

        public override Empty SetProposalIdForDevelopersAssociation(Hash input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.AssociationContract.Value, "token contract permission");
            if (State.ProposalIds.Value == null)
                State.ProposalIds.Value = new ProposalIds();
            State.ProposalIds.Value.ProposalIdFromDeveloperAssociation.Add(input);
            return new Empty();
        }
        
        public override Empty SetProposalIdForUserAssociation(Hash input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.AssociationContract.Value, "token contract permission");
            if (State.ProposalIds.Value == null)
                State.ProposalIds.Value = new ProposalIds();
            State.ProposalIds.Value.ProposalIdFromUserAssociation.Add(input);
            return new Empty();
        }
        
        public override Empty SetProposalIdForDevelopers(Hash input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.AssociationContract.Value, "token contract permission");
            if (State.ProposalIds.Value == null)
                State.ProposalIds.Value = new ProposalIds();
            State.ProposalIds.Value.ProposalIdFromDevelopers.Add(input);
            return new Empty();
        }

        public override Empty SetProposalIdForReferendum(Hash input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.ReferendumContract.Value, "token contract permission");
            if (State.ProposalIds.Value == null)
                State.ProposalIds.Value = new ProposalIds();
            State.ProposalIds.Value.ProposalIdFromReferendum.Add(input);
            return new Empty();
        }
        
        public override Empty RemoveProposalId(Hash input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            if (State.ProposalIds.Value == null)
                State.ProposalIds.Value = new ProposalIds();
            if (State.ProposalIds.Value.ProposalIdFromDevelopers.Contains(input))
                State.ProposalIds.Value.ProposalIdFromDevelopers.Remove(input);
            if (State.ProposalIds.Value.ProposalIdFromDeveloperAssociation.Contains(input))
                State.ProposalIds.Value.ProposalIdFromDeveloperAssociation.Remove(input);
            if (State.ProposalIds.Value.ProposalIdFromReferendum.Contains(input))
                State.ProposalIds.Value.ProposalIdFromReferendum.Remove(input);
            if (State.ProposalIds.Value.ProposalIdFromUserAssociation.Contains(input))
                State.ProposalIds.Value.ProposalIdFromUserAssociation.Remove(input);
            return new Empty();
        }

        public override Empty TransferCreateProposalForUserFee(CoefficientFromSender input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            AssertFromUserFeeLeafController();
            var proposalInput = new CreateProposalBySystemContractInput
            {
                OriginProposer = Context.Sender,
                ProposalInput = new CreateProposalInput
                {
                    ToAddress = Context.Self,
                    OrganizationAddress = State.ControllerForUserFee.Value.RootController,
                    ContractMethodName = nameof(UpdateCoefficientFromSender),
                    Params = input.ToByteString(),
                    ExpiredTime = GetDefaultDueDate().AddDays(1)
                },
                ProposalIdFeedbackMethod = nameof(SetProposalIdForUserAssociation)
            };
            State.AssociationContract.CreateProposalBySystemContract.Send(proposalInput);
            return new Empty(); 
        }
        
        public override Empty TransferCreateProposalForDeveloperFee(CoefficientFromContract input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            AssertFromDeveloperFeeLeafController();
            var proposalInput = new CreateProposalBySystemContractInput
            {
                OriginProposer = Context.Sender,
                ProposalInput = new CreateProposalInput
                {
                    ToAddress = Context.Self,
                    OrganizationAddress = State.ControllerForDeveloperFee.Value.RootController,
                    ContractMethodName = nameof(UpdateCoefficientFromContract),
                    Params = input.ToByteString(),
                    ExpiredTime = GetDefaultDueDate().AddDays(1)
                },
                ProposalIdFeedbackMethod = nameof(SetProposalIdForDevelopersAssociation)
            };
            State.AssociationContract.CreateProposalBySystemContract.Send(proposalInput);
            return new Empty(); 
        }

        public override Empty ReleaseProposalForAssociation(Hash id)
        {
            State.AssociationContract.Release.Send(id);
            return new Empty();
        }
        
        public override Empty ReleaseProposalForReferendum(Hash id)
        {
            State.AssociationContract.Release.Send(id);
            return new Empty();
        }
        
        public override Empty TransferToMiddleControllerForDeveloperFee(MiddleProposal input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.ControllerForDeveloperFee.Value.ParliamentController, "no permission");
            
            var proposalInput = new CreateProposalBySystemContractInput
            {
                OriginProposer = Context.Sender,
                ProposalInput = new CreateProposalInput
                {
                    ToAddress = input.ToAddress,
                    OrganizationAddress = input.OrganizationAddress,
                    ContractMethodName = input.ContractMethodName,
                    Params = input.Params,
                    ExpiredTime = input.ExpiredTime
                },
                ProposalIdFeedbackMethod = nameof(SetProposalIdForDevelopers)
            };
            State.AssociationContract.CreateProposalBySystemContract.Send(proposalInput);
            return new Empty();
        }
        
        public override Empty TransferToMiddleControllerForUserFee(MiddleProposal input)
        {
            Assert(ControllersInitialized(), "controller does not exist");
            Assert(Context.Sender == State.ControllerForUserFee.Value.ParliamentController, "no permission");
            
            var proposalInput = new CreateProposalBySystemContractInput
            {
                OriginProposer = Context.Sender,
                ProposalInput = new CreateProposalInput
                {
                    ToAddress = input.ToAddress,
                    OrganizationAddress = input.OrganizationAddress,
                    ContractMethodName = input.ContractMethodName,
                    Params = input.Params,
                    ExpiredTime = input.ExpiredTime
                },
                ProposalIdFeedbackMethod = nameof(SetProposalIdForReferendum)
            };
            State.ReferendumContract.CreateProposalBySystemContract.Send(proposalInput);
            return new Empty(); ;
        }
        
        private bool ControllersInitialized()
        {
            if(State.ControllerForDeveloperFee.Value == null)
                State.ControllerForDeveloperFee.Value = new ControllerForDeveloperFee();
            if(State.ControllerForUserFee.Value == null)
                State.ControllerForUserFee.Value = new ControllerForUserFee();
            return !(State.ControllerForDeveloperFee.Value.DeveloperController == null ||
                    State.ControllerForDeveloperFee.Value.ParliamentController == null ||
                    State.ControllerForDeveloperFee.Value.RootController == null ||
                    State.ControllerForUserFee.Value.ParliamentController == null ||
                    State.ControllerForUserFee.Value.ReferendumController == null ||
                    State.ControllerForUserFee.Value.RootController == null);
        }

        private void AssertFromDeveloperFeeLeafController()
        {
            Assert(Context.Sender == State.ControllerForDeveloperFee.Value.DeveloperController || 
                   Context.Sender == State.ControllerForDeveloperFee.Value.ParliamentController, "no permission");
        }
        private void AssertFromUserFeeLeafController()
        {
            Assert(Context.Sender == State.ControllerForUserFee.Value.ReferendumController || 
                   Context.Sender == State.ControllerForUserFee.Value.ParliamentController, "no permission");
        }
        private void CalculateDeveloperFeeController()
        {
            State.ControllerForDeveloperFee.Value = new ControllerForDeveloperFee();
            State.ControllerForDeveloperFee.Value.ParliamentController = State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());;
            State.ControllerForDeveloperFee.Value.DeveloperController =
                State.AssociationContract.CalculateOrganizationAddress.Call(GetDeveloperOrganization()
                    .OrganizationCreationInput);
            State.ControllerForDeveloperFee.Value.RootController =
                State.AssociationContract.CalculateOrganizationAddress.Call(GetAssociationOrganizationForDeveloperFee()
                    .OrganizationCreationInput);
        }

        private void CalculateUserFeeController()
        {
            State.ControllerForUserFee.Value = new ControllerForUserFee();
            State.ControllerForUserFee.Value.ParliamentController = State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());;
            State.ControllerForUserFee.Value.ReferendumController =
                State.ReferendumContract.CalculateOrganizationAddress.Call(GetReferendumOrganizationForUserFee()
                    .OrganizationCreationInput);
            State.ControllerForUserFee.Value.RootController =
                State.AssociationContract.CalculateOrganizationAddress.Call(GetAssociationOrganizationForUserFee()
                    .OrganizationCreationInput);
        }
        private void CreateReferendumOrganizationForUserFee()
        {
            State.ReferendumContract.CreateOrganizationBySystemContract.Send(GetReferendumOrganizationForUserFee());
        }

        private void CreateAssociationOrganizationForUserFee()
        {
            State.AssociationContract.CreateOrganizationBySystemContract.Send(GetAssociationOrganizationForUserFee());
        }
        
        private void CreateDeveloperOrganization()
        {
            State.AssociationContract.CreateOrganizationBySystemContract.Send(GetDeveloperOrganization());
        }

        private void CreateAssociationOrganizationForDeveloperFee()
        {
            State.AssociationContract.CreateOrganizationBySystemContract.Send(GetAssociationOrganizationForDeveloperFee());
        }

        #endregion

        #region organization create input
        private Referendum.CreateOrganizationBySystemContractInput GetReferendumOrganizationForUserFee()
        {
            var parliamentOrg = State.ControllerForUserFee.Value.ParliamentController;
            var whiteList = new List<Address> {parliamentOrg};
            return new Referendum.CreateOrganizationBySystemContractInput
            {
                OrganizationCreationInput = new Referendum.CreateOrganizationInput
                {
                    TokenSymbol = "EE",
                    ProposalReleaseThreshold = new ProposalReleaseThreshold
                    {
                        MinimalApprovalThreshold = 1,
                        MinimalVoteThreshold = 1,
                        MaximalRejectionThreshold = 0,
                        MaximalAbstentionThreshold = 0
                    },
                    ProposerWhiteList = new ProposerWhiteList
                    {
                        Proposers = {whiteList}
                    }
                }
            };
        }

        private Association.CreateOrganizationBySystemContractInput GetAssociationOrganizationForUserFee()
        {
            var parliamentOrg = State.ControllerForUserFee.Value.ParliamentController;
            var proposers = new List<Address>
                {State.ControllerForUserFee.Value.ReferendumController, parliamentOrg};
            return new Association.CreateOrganizationBySystemContractInput
            {
                OrganizationCreationInput = new Association.CreateOrganizationInput
                {
                    OrganizationMemberList = new Association.OrganizationMemberList
                    {
                        OrganizationMembers = {proposers}
                    },
                    ProposalReleaseThreshold = new ProposalReleaseThreshold
                    {
                        MinimalApprovalThreshold = proposers.Count,
                        MinimalVoteThreshold = proposers.Count,
                        MaximalRejectionThreshold = 0,
                        MaximalAbstentionThreshold = 0
                    },
                    ProposerWhiteList = new ProposerWhiteList
                    {
                        Proposers = {proposers}
                    }
                }
            };
        }
        private Association.CreateOrganizationBySystemContractInput GetDeveloperOrganization()
        {
            var parliamentOrganization = State.ControllerForDeveloperFee.Value.ParliamentController;
            var proposers = new List<Address> {parliamentOrganization};
            return new Association.CreateOrganizationBySystemContractInput
            {
                OrganizationCreationInput = new Association.CreateOrganizationInput
                {
                    OrganizationMemberList = new Association.OrganizationMemberList
                    {
                        OrganizationMembers = {proposers}
                    },
                    ProposalReleaseThreshold = new ProposalReleaseThreshold
                    {
                        MinimalApprovalThreshold = proposers.Count,
                        MinimalVoteThreshold = proposers.Count,
                        MaximalRejectionThreshold = 0,
                        MaximalAbstentionThreshold = 0
                    },
                    ProposerWhiteList = new ProposerWhiteList
                    {
                        Proposers = {proposers}
                    }
                }
            };
        }

        private Association.CreateOrganizationBySystemContractInput GetAssociationOrganizationForDeveloperFee()
        {
            var parliamentOrg = State.ControllerForDeveloperFee.Value.ParliamentController;
            var proposers = new List<Address>
            {
                State.ControllerForDeveloperFee.Value.DeveloperController, parliamentOrg
            };
            var actualProposalCount = proposers.Count;
            return new Association.CreateOrganizationBySystemContractInput
            {
                OrganizationCreationInput = new Association.CreateOrganizationInput
                {
                    OrganizationMemberList = new Association.OrganizationMemberList
                    {
                        OrganizationMembers = {proposers}
                    },
                    ProposalReleaseThreshold = new ProposalReleaseThreshold
                    {
                        MinimalApprovalThreshold = actualProposalCount,
                        MinimalVoteThreshold = actualProposalCount,
                        MaximalRejectionThreshold = 0,
                        MaximalAbstentionThreshold = 0
                    },
                    ProposerWhiteList = new ProposerWhiteList
                    {
                        Proposers = {proposers}
                    }
                }
            };
        }
        #endregion

        private Timestamp GetDefaultDueDate()
        {
            return DateTime.UtcNow.ToTimestamp();
        }
    }
}