using System.Threading.Tasks;
using Acs1;
using Acs3;
using AElf.Contracts.Parliament;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace AElf.Contracts.TokenConverter
{
    public partial class TokenConverterContractTests
    {
        [Fact]
        public async Task ChangeMethodFeeController_Test()
        {
            var createOrganizationResult =
                await ParliamentContractStub.CreateOrganization.SendAsync(
                    new CreateOrganizationInput
                    {
                        ProposalReleaseThreshold = new ProposalReleaseThreshold
                        {
                            MinimalApprovalThreshold = 1000,
                            MinimalVoteThreshold = 1000
                        }
                    });
            var organizationAddress = Address.Parser.ParseFrom(createOrganizationResult.TransactionResult.ReturnValue);

            var methodFeeController = await DefaultStub.GetMethodFeeController.CallAsync(new Empty());
            var defaultOrganization =
                await ParliamentContractStub.GetDefaultOrganizationAddress.CallAsync(
                    new Empty());
            methodFeeController.OwnerAddress.ShouldBe(defaultOrganization);

            const string proposalCreationMethodName = nameof(DefaultStub.ChangeMethodFeeController);
//            var proposalId = await CreateProposalAsync(TokenConverterContractAddress,
//                methodFeeController.OwnerAddress, proposalCreationMethodName, new AuthorityInfo
//                {
//                    OwnerAddress = organizationAddress,
//                    ContractAddress = ParliamentContractAddress
//                });
//            await ApproveWithMinersAsync(proposalId);
//            var releaseResult = await ParliamentContractStub.Release.SendAsync(proposalId);
//            releaseResult.TransactionResult.Error.ShouldBeNullOrEmpty();
//            releaseResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var newMethodFeeController = await DefaultStub.GetMethodFeeController.CallAsync(new Empty());
            newMethodFeeController.OwnerAddress.ShouldBe(organizationAddress);
        }

    }
}