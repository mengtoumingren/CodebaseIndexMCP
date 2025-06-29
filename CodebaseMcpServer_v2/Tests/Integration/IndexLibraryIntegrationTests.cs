using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.IndexManagement.Commands;
using CodebaseMcpServer.Application.IndexManagement.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CodebaseMcpServer.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Trait("TestCategory", "IndexLibrary")]
    public class IndexLibraryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        
        public IndexLibraryIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }
        
        [Fact]
        public async Task CreateIndexLibrary_EndToEnd_ShouldWork()
        {
            // Arrange
            var request = new CreateIndexLibraryCommand(
                CodebasePath: @"C:\TestProject",
                CollectionName: "Integration Test Library");
            
            // Act
            var response = await _client.PostAsJsonAsync("/api/index-libraries", request);
            
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            
            var content = await response.Content.ReadFromJsonAsync<IndexLibraryDto>(); // Assuming IndexLibraryDto is the response type
            content.Should().NotBeNull();
            content!.Id.Should().BeGreaterThan(0);
        }
    }
}