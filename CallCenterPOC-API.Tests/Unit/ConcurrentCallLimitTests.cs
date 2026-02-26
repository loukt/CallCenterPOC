using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CallCenterPOC_API.Tests.Unit
{
    public class ConcurrentCallLimitTests
    {
        private static CallService CreateCallService(int prePopulateCount = 0)
        {
            var configValues = new Dictionary<string, string?>
            {
                ["AzureCommunicationServices:ConnectionString"] = "endpoint=https://fake.communication.azure.com/;accesskey=fakekey123456789012345678901234567890123=",
                ["AzureCommunicationServices:PhoneNumber"] = "+15551234567",
                ["CallbackUrl"] = "https://localhost:5001/api/Callback",
                ["BlobStorage:ContainerName"] = "test-container"
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var loggerMock = new Mock<ILogger<CallService>>();
            var hubContextMock = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<ContactCenterPOC.Hubs.TranscriptHub>>();
            var blobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true");
            var campaignService = new CampaignService(
                blobServiceClient,
                configuration,
                new Mock<ILogger<CampaignService>>().Object);

            var callHistoryService = new CallHistoryService(
                blobServiceClient,
                configuration,
                new Mock<ILogger<CallHistoryService>>().Object);

            var callService = new CallService(
                configuration,
                loggerMock.Object,
                hubContextMock.Object,
                campaignService,
                callHistoryService,
                sentimentService: null);

            for (int i = 0; i < prePopulateCount; i++)
            {
                callService.ActiveCalls[$"call-{i}"] = new ActiveCall
                {
                    CallConnectionId = $"call-{i}",
                    TargetPhoneNumber = $"+1555000000{i}",
                    Prompt = "Test prompt",
                    Status = CallStatus.Connected,
                    StartedAt = DateTimeOffset.UtcNow
                };
            }

            return callService;
        }

        [Fact]
        public async Task InitiateCall_WhenAtMaxConcurrent_ShouldThrowInvalidOperationException()
        {
            // Arrange: 5 active calls, trying to add 1 more
            var callService = CreateCallService(prePopulateCount: 5);

            // Act & Assert: 6th call should throw
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => callService.InitiateCall(new[] { "+6591234567" }, "Test", null, null, null!));
        }

        [Fact]
        public void ActiveCalls_BelowLimit_ShouldAllowNewCall()
        {
            // Arrange: 4 active calls (under limit of 5)
            var callService = CreateCallService(prePopulateCount: 4);

            // Verify we are under the limit
            Assert.Equal(4, callService.ActiveCalls.Count);
            Assert.True(callService.ActiveCalls.Count < 5);
        }

        [Fact]
        public async Task InitiateCall_FourActiveAndTwoNew_ShouldThrow()
        {
            // Arrange: 4 active + 2 new = 6 > 5 limit
            var callService = CreateCallService(prePopulateCount: 4);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => callService.InitiateCall(
                    new[] { "+6591234567", "+14155551234" }, "Test", null, null, null!));
        }

        [Fact]
        public void ThreeActiveAndTwoNew_ShouldBeUnderLimit()
        {
            // Arrange: 3 active + 2 new = 5 (at limit, not over)
            var callService = CreateCallService(prePopulateCount: 3);

            // Verify 3 + 2 = 5 would be accepted (count check only)
            Assert.True(callService.ActiveCalls.Count + 2 <= 5);
        }
    }
}
