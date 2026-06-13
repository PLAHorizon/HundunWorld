using System;
using System.Threading.Tasks;
using Xunit;
using Orleans;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.IM.Message.Network;

namespace Horizon.IM.Gateway.Tests
{
    [Collection("OrleansCluster")]
    public class IMUserGrainContactSearchTests : IAsyncLifetime
    {
        private TestCluster _cluster;

        public async Task InitializeAsync()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<IMGatewayTestSiloConfigurator>();

            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }

        public async Task DisposeAsync()
        {
            if (_cluster != null)
            {
                await _cluster.StopAllSilosAsync();
                await _cluster.DisposeAsync();
            }
        }

        [Fact]
        public async Task SearchContactAsync_ShouldNotThrowEFTranslationException_WhenProvidingKeyword()
        {
            // Arrange
            var testUserId = Guid.NewGuid();
            var grain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(testUserId);
            var searchRequest = new IMContactSearchRequest
            {
                Keyword = "testUser",
                Limit = 10,
                Offset = 0
            };

            // Act
            // If the EF translation fails, this will throw an InvalidOperationException.
            var response = await grain.SearchContactAsync(searchRequest);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            // Verify that the query returned successfully without blowing up on 'Contains' expression
            Assert.True(response.Results.Count >= 0);
        }
    }
}
