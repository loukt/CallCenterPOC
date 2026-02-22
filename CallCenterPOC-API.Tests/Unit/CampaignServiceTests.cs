using Azure.Storage.Blobs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CallCenterPOC_API.Tests.Unit
{
    public class CampaignServiceTests
    {
        private static CampaignService CreateService(
            Mock<BlobServiceClient>? blobMock = null,
            IConfiguration? config = null)
        {
            blobMock ??= new Mock<BlobServiceClient>();
            var containerMock = new Mock<Azure.Storage.Blobs.BlobContainerClient>();
            var blobClientMock = new Mock<Azure.Storage.Blobs.BlobClient>();

            // Setup chain: BlobServiceClient -> GetBlobContainerClient -> GetBlobClient
            blobMock.Setup(b => b.GetBlobContainerClient(It.IsAny<string>()))
                .Returns(containerMock.Object);
            containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>()))
                .Returns(blobClientMock.Object);
            containerMock.Setup(c => c.CreateIfNotExistsAsync(default, default, default, default))
                .ReturnsAsync((Azure.Response<Azure.Storage.Blobs.Models.BlobContainerInfo>)null!);

            // Simulate blob not existing (first load = defaults)
            blobClientMock.Setup(b => b.ExistsAsync(default))
                .ReturnsAsync(Azure.Response.FromValue(false, null!));

            config ??= new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlobStorage:ContainerName"] = "callcenter-data"
                })
                .Build();

            var loggerMock = new Mock<ILogger<CampaignService>>();

            return new CampaignService(blobMock.Object, config, loggerMock.Object);
        }

        [Fact]
        public async Task GetAllAsync_ShouldReturn6DefaultCampaigns()
        {
            // Arrange
            var service = CreateService();

            // Act
            var campaigns = await service.GetAllAsync();

            // Assert
            Assert.Equal(6, campaigns.Count);
            Assert.All(campaigns, c => Assert.True(c.IsDefault));
        }

        [Fact]
        public async Task GetAllAsync_DefaultCampaigns_ShouldHaveExpectedTitles()
        {
            var service = CreateService();
            var campaigns = await service.GetAllAsync();

            var titles = campaigns.Select(c => c.Title).ToList();
            Assert.Contains("Bank Loan Collection", titles);
            Assert.Contains("New Product Marketing", titles);
            Assert.Contains("Customer Satisfaction Survey", titles);
            Assert.Contains("Appointment Reminder", titles);
            Assert.Contains("Insurance Policy Renewal", titles);
            Assert.Contains("Subscription Renewal & Upsell", titles);
        }

        [Fact]
        public async Task GetAllAsync_DefaultCampaigns_ShouldHaveDetailedInstructions()
        {
            var service = CreateService();
            var campaigns = await service.GetAllAsync();

            Assert.All(campaigns, c =>
            {
                Assert.NotNull(c.AiBehaviorInstructions);
                Assert.True(c.AiBehaviorInstructions.Length >= 100,
                    $"Campaign '{c.Title}' instructions too short ({c.AiBehaviorInstructions.Length} chars)");
            });
        }

        [Fact]
        public async Task CreateAsync_WithDuplicateTitle_ShouldThrow()
        {
            var service = CreateService();

            // Default campaigns include "Bank Loan Collection"
            var request = new CreateCampaignRequest
            {
                Title = "Bank Loan Collection",
                Description = "Duplicate",
                AiBehaviorInstructions = "Test"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CreateAsync(request));
        }

        [Fact]
        public async Task CreateAsync_WithValidRequest_ShouldAddToCampaignList()
        {
            var service = CreateService();

            var request = new CreateCampaignRequest
            {
                Title = "New Campaign",
                Description = "A brand new campaign",
                AiBehaviorInstructions = "Be helpful and professional"
            };

            var created = await service.CreateAsync(request);

            Assert.NotNull(created);
            Assert.Equal("New Campaign", created.Title);
            Assert.False(created.IsDefault);

            var all = await service.GetAllAsync();
            Assert.Equal(7, all.Count);
            Assert.Contains(all, c => c.Title == "New Campaign");
        }

        [Fact]
        public async Task GetByIdAsync_WithValidId_ShouldReturnCampaign()
        {
            var service = CreateService();

            var campaigns = await service.GetAllAsync();
            var first = campaigns.First();

            var found = await service.GetByIdAsync(first.Id);

            Assert.NotNull(found);
            Assert.Equal(first.Id, found!.Id);
            Assert.Equal(first.Title, found.Title);
        }

        [Fact]
        public async Task GetByIdAsync_WithInvalidId_ShouldReturnNull()
        {
            var service = CreateService();

            var found = await service.GetByIdAsync("nonexistent-id");

            Assert.Null(found);
        }
    }
}
