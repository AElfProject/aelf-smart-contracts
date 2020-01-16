using System.Linq;
using System.Threading.Tasks;
using Acs3;
using AElf.Contracts.Association;
using AElf.Contracts.Parliament;
using AElf.Contracts.Referendum;
using AElf.Kernel;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Volo.Abp.Threading;
using Xunit;

namespace AElf.Contracts.MultiToken
{
    public class MultiTokenAuthorizedTest : MultiTokenContractCrossChainTestBase
    {
        public MultiTokenAuthorizedTest()
        {
            AsyncHelper.RunSync(InitializeTokenContract);
        }

        private async Task InitializeTokenContract()
        {
            var callOwner = Address.FromPublicKey(MainChainTester.KeyPair.PublicKey);
            var initResult = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.Initialize), new InitializeInput
                {
                    DefaultProposer = callOwner
                });
            initResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var initOrgResult = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.InitializeAuthorizedOrganization), new Empty());
            initOrgResult.Status.ShouldBe(TransactionResultStatus.Mined);
        }

        [Fact]
        public async Task Update_Coefficient_For_Sender_Success()
        {
            const string defaultSymbol = "EE";
            const int pieceKey = 1000000;
            var callOwner = Address.FromPublicKey(MainChainTester.KeyPair.PublicKey);
            var createInput = new CreateInput
            {
                Symbol = defaultSymbol,
                TokenName = defaultSymbol,
                Decimals = 2,
                IsBurnable = true,
                TotalSupply = 10000_0000,
                Issuer = callOwner,
                LockWhiteList = {ReferendumAddress}
            };
            var createRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.Create), createInput);
            createRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var issueResult = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.Issue), new IssueInput
                {
                    Amount = 500000,
                    To = callOwner,
                    Symbol = defaultSymbol
                });
            issueResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var approveResult = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.Approve), new ApproveInput
                {
                    Spender = ReferendumAddress,
                    Symbol = defaultSymbol,
                    Amount = 100000
                });
            approveResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var organizationInfoRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.GetUserFeeController), new Empty());
            organizationInfoRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var organizationInfo = new ControllerForUserFee();
            organizationInfo.MergeFrom(organizationInfoRet.ReturnValue);

            var updateInput = new CoefficientFromSender
            {
                LinerCoefficient = new LinerCoefficient
                {
                    ConstantValue = 1,
                    Denominator = 2,
                    Numerator = 3
                },
                PieceKey = pieceKey,
                IsLiner = true
            };
            var associationCreateProposalInput = new CreateProposalInput
            {
                ToAddress = TokenContractAddress,
                OrganizationAddress = organizationInfo.RootController,
                Params = updateInput.ToByteString(),
                ContractMethodName = nameof(TokenContractContainer.TokenContractStub.UpdateCoefficientFromSender),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var associationCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.CreateProposal),
                associationCreateProposalInput);
            associationCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var associationProposalId = new Hash();
            associationProposalId.MergeFrom(associationCreateProposal.ReturnValue);

            var referendumCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.ReferendumController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var referendumCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(ReferendumAddress,
                nameof(ReferendumContractContainer.ReferendumContractStub.CreateProposal),
                referendumCreateProposalInput);
            referendumCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var referendumProposalId = new Hash();
            referendumProposalId.MergeFrom(referendumCreateProposal.ReturnValue);

            var parliamentCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.ParliamentController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var parliamentCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(ParliamentAddress,
                nameof(ParliamentContractContainer.ParliamentContractStub.CreateProposal),
                parliamentCreateProposalInput);
            parliamentCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var parliamentProposalId = new Hash();
            parliamentProposalId.MergeFrom(parliamentCreateProposal.ReturnValue);

            var referendumApprove = await MainChainTester.ExecuteContractWithMiningAsync(ReferendumAddress,
                nameof(ReferendumContractContainer.ReferendumContractStub.Approve), referendumProposalId);
            referendumApprove.Status.ShouldBe(TransactionResultStatus.Mined);

            await ApproveWithMinersAsync(parliamentProposalId, ParliamentAddress, MainChainTester);

            await ReleaseProposalAsync(parliamentProposalId, ParliamentAddress, MainChainTester);
            var referendumRelease = await MainChainTester.ExecuteContractWithMiningAsync(ReferendumAddress,
                nameof(ReferendumContractContainer.ReferendumContractStub.Release), referendumProposalId);
            referendumRelease.Status.ShouldBe(TransactionResultStatus.Mined);
            var associationRelease = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Release), associationProposalId);
            associationRelease.Status.ShouldBe(TransactionResultStatus.Mined);

            var userCoefficientRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.GetCalculateFeeCoefficientOfSender), new Empty());
            userCoefficientRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var userCoefficient = new CalculateFeeCoefficientsOfType();
            userCoefficient.MergeFrom(userCoefficientRet.ReturnValue);
            var hasModified = userCoefficient.Coefficients.Single(x => x.PieceKey == pieceKey);
            hasModified.CoefficientDic["ConstantValue".ToLower()].ShouldBe(1);
            hasModified.CoefficientDic["Denominator".ToLower()].ShouldBe(2);
            hasModified.CoefficientDic["Numerator".ToLower()].ShouldBe(3);
        }

        [Fact]
        public async Task Update_Coefficient_For_Contract_Success()
        {
            const int pieceKey = 1000000;
            const FeeTypeEnum feeType = FeeTypeEnum.Traffic;
            var organizationInfoRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.GetDeveloperFeeController), new Empty());
            organizationInfoRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var organizationInfo = new ControllerForDeveloperFee();
            organizationInfo.MergeFrom(organizationInfoRet.ReturnValue);

            var updateInput = new CoefficientFromContract
            {
                FeeType = feeType,
                Coefficient = new CoefficientFromSender
                {
                    LinerCoefficient = new LinerCoefficient
                    {
                        ConstantValue = 1,
                        Denominator = 2,
                        Numerator = 3
                    },
                    PieceKey = pieceKey,
                    IsLiner = true
                }
            };
            var associationCreateProposalInput = new CreateProposalInput
            {
                ToAddress = TokenContractAddress,
                OrganizationAddress = organizationInfo.RootController,
                Params = updateInput.ToByteString(),
                ContractMethodName = nameof(TokenContractContainer.TokenContractStub.UpdateCoefficientFromContract),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var associationCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.CreateProposal),
                associationCreateProposalInput);
            associationCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var associationProposalId = new Hash();
            associationProposalId.MergeFrom(associationCreateProposal.ReturnValue);

            var developerCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.DeveloperController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var developerCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.CreateProposal),
                developerCreateProposalInput);
            developerCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var developerProposalId = new Hash();
            developerProposalId.MergeFrom(developerCreateProposal.ReturnValue);

            var parliamentCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.ParliamentController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var parliamentCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(ParliamentAddress,
                nameof(ParliamentContractContainer.ParliamentContractStub.CreateProposal),
                parliamentCreateProposalInput);
            parliamentCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var parliamentProposalId = new Hash();
            parliamentProposalId.MergeFrom(parliamentCreateProposal.ReturnValue);

            var developerApprove = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Approve), developerProposalId);
            developerApprove.Status.ShouldBe(TransactionResultStatus.Mined);

            await ApproveWithMinersAsync(parliamentProposalId, ParliamentAddress, MainChainTester);

            await ReleaseProposalAsync(parliamentProposalId, ParliamentAddress, MainChainTester);

            var developerRelease = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Release), developerProposalId);
            developerRelease.Status.ShouldBe(TransactionResultStatus.Mined);

            var associationRelease = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Release), associationProposalId);
            associationRelease.Status.ShouldBe(TransactionResultStatus.Mined);

            var userCoefficientRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.GetCalculateFeeCoefficientOfContract), new SInt32Value
                {
                    Value = (int) feeType
                });
            userCoefficientRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var userCoefficient = new CalculateFeeCoefficientsOfType();
            userCoefficient.MergeFrom(userCoefficientRet.ReturnValue);
            var hasModified = userCoefficient.Coefficients.Single(x => x.PieceKey == pieceKey);
            hasModified.CoefficientDic["ConstantValue".ToLower()].ShouldBe(1);
            hasModified.CoefficientDic["Denominator".ToLower()].ShouldBe(2);
            hasModified.CoefficientDic["Numerator".ToLower()].ShouldBe(3);
        }

        [Fact]
        public async Task Update_Coefficient_For_Contract_Failed()
        {
            const int pieceKey = 1000000;
            const FeeTypeEnum feeType = FeeTypeEnum.Traffic;
            var organizationInfoRet = await MainChainTester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContractContainer.TokenContractStub.GetDeveloperFeeController), new Empty());
            organizationInfoRet.Status.ShouldBe(TransactionResultStatus.Mined);
            var organizationInfo = new ControllerForDeveloperFee();
            organizationInfo.MergeFrom(organizationInfoRet.ReturnValue);

            var updateInput = new CoefficientFromContract
            {
                FeeType = feeType,
                Coefficient = new CoefficientFromSender
                {
                    LinerCoefficient = new LinerCoefficient
                    {
                        ConstantValue = 1,
                        Denominator = 2,
                        Numerator = 3
                    },
                    PieceKey = pieceKey,
                    IsLiner = true
                }
            };
            var associationCreateProposalInput = new CreateProposalInput
            {
                ToAddress = TokenContractAddress,
                OrganizationAddress = organizationInfo.RootController,
                Params = updateInput.ToByteString(),
                ContractMethodName = nameof(TokenContractContainer.TokenContractStub.UpdateCoefficientFromContract),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var associationCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.CreateProposal),
                associationCreateProposalInput);
            associationCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var associationProposalId = new Hash();
            associationProposalId.MergeFrom(associationCreateProposal.ReturnValue);

            var developerCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.DeveloperController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var developerCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.CreateProposal),
                developerCreateProposalInput);
            developerCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var developerProposalId = new Hash();
            developerProposalId.MergeFrom(developerCreateProposal.ReturnValue);

            var parliamentCreateProposalInput = new CreateProposalInput
            {
                ToAddress = AssociationAddress,
                OrganizationAddress = organizationInfo.ParliamentController,
                Params = associationProposalId.ToByteString(),
                ContractMethodName = nameof(AssociationContractContainer.AssociationContractStub.Approve),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1)
            };
            var parliamentCreateProposal = await MainChainTester.ExecuteContractWithMiningAsync(ParliamentAddress,
                nameof(ParliamentContractContainer.ParliamentContractStub.CreateProposal),
                parliamentCreateProposalInput);
            parliamentCreateProposal.Status.ShouldBe(TransactionResultStatus.Mined);
            var parliamentProposalId = new Hash();
            parliamentProposalId.MergeFrom(parliamentCreateProposal.ReturnValue);

            var developerApprove = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Approve), developerProposalId);
            developerApprove.Status.ShouldBe(TransactionResultStatus.Mined);

            await ApproveWithMinersAsync(parliamentProposalId, ParliamentAddress, MainChainTester);

            await ReleaseProposalAsync(parliamentProposalId, ParliamentAddress, MainChainTester);

            var associationFailedRelease = await MainChainTester.ExecuteContractWithMiningAsync(AssociationAddress,
                nameof(AssociationContractContainer.AssociationContractStub.Release), associationProposalId);
            associationFailedRelease.Status.ShouldBe(TransactionResultStatus.Failed);
        }
    }
}