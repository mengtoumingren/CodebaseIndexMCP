using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.IndexManagement.Commands;
using CodebaseMcpServer.Application.IndexManagement.Services;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Domain.IndexManagement.Aggregates;
using CodebaseMcpServer.Domain.IndexManagement.Repositories;
using CodebaseMcpServer.Domain.IndexManagement.Services;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.Shared; // Added this line
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using static Moq.Times;

namespace CodebaseMcpServer.Tests.Application.IndexManagement
{
    [Trait("Category", "Application")]
    [Trait("TestCategory", "ApplicationService")]
    public class IndexLibraryApplicationServiceTests
    {
        private readonly Mock<IIndexLibraryRepository> _repositoryMock;
        private readonly Mock<IProjectTypeDetectionService> _detectionServiceMock;
        private readonly Mock<IIndexingService> _indexingServiceMock;
        private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
        private readonly IndexLibraryApplicationService _service;
        private readonly Mock<ILogger<IndexLibraryApplicationService>> _loggerMock;

        public IndexLibraryApplicationServiceTests()
        {
            _repositoryMock = new Mock<IIndexLibraryRepository>();
            _detectionServiceMock = new Mock<IProjectTypeDetectionService>();
            _indexingServiceMock = new Mock<IIndexingService>();
            _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
            _loggerMock = new Mock<ILogger<IndexLibraryApplicationService>>();

            _service = new IndexLibraryApplicationService(
                _repositoryMock.Object,
                _detectionServiceMock.Object,
                _indexingServiceMock.Object,
                _eventDispatcherMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task CreateIndexLibrary_WithValidRequest_ShouldCreateAndSaveLibrary()
        {
            // Arrange
            var command = new CreateIndexLibraryCommand(
                @"C:\TestProject",
                "Test Collection");

            _repositoryMock.Setup(r => r.GetByCodebasePathAsync(It.IsAny<CodebasePath>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IndexLibrary?)null);
            _repositoryMock.Setup(r => r.GetNextIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            _detectionServiceMock.Setup(d => d.DetectProjectTypeAsync(It.IsAny<CodebasePath>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CodebaseMcpServer.Domain.IndexManagement.Enums.ProjectType.CSharp); // Assuming ProjectType enum exists

            // Act
            var result = await _service.HandleAsync(command);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(1);
            _repositoryMock.Verify(r => r.AddAsync(It.IsAny<IndexLibrary>(), It.IsAny<CancellationToken>()), Once);
            _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Once);
            _eventDispatcherMock.Verify(d => d.DispatchAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()), Once);
        }

        [Fact]
        public async Task CreateIndexLibrary_WithExistingPath_ShouldReturnFailure()
        {
            // Arrange
            var command = new CreateIndexLibraryCommand(
                @"C:\ExistingProject",
                "Existing Collection");

            _repositoryMock.Setup(r => r.GetByCodebasePathAsync(It.IsAny<CodebasePath>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexLibrary(new IndexLibraryId(1), new CodebasePath(@"C:\ExistingProject"), new CollectionName("Existing Collection")));

            // Act
            var result = await _service.HandleAsync(command);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Contain("Codebase path already exists");
            _repositoryMock.Verify(r => r.AddAsync(It.IsAny<IndexLibrary>(), It.IsAny<CancellationToken>()), Never);
        }

        [Fact]
        public async Task StartIndexing_ExistingLibrary_ShouldUpdateStatusAndDispatchEvent()
        {
            // Arrange
            var libraryId = 1;
            var command = new StartIndexingCommand(libraryId);
            var library = new IndexLibrary(new IndexLibraryId(libraryId), new CodebasePath(@"C:\TestProject"), new CollectionName("Test Collection"));

            _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<IndexLibraryId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(library);
            _indexingServiceMock.Setup(s => s.IndexCodebaseAsync(It.IsAny<CodebasePath>(), It.IsAny<CodebaseMcpServer.Domain.IndexManagement.ValueObjects.IndexConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CodebaseMcpServer.Domain.IndexManagement.ValueObjects.IndexResult(10, 10, 0, 10, 1000L, TimeSpan.FromSeconds(10), null));

            // Act
            var result = await _service.HandleAsync(command);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _repositoryMock.Verify(r => r.UpdateAsync(It.Is<IndexLibrary>(l => l.Status == CodebaseMcpServer.Domain.IndexManagement.Enums.IndexStatus.Indexing), It.IsAny<CancellationToken>()), Once);
            _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Once);
            _eventDispatcherMock.Verify(d => d.DispatchAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()), Once);
        }

        [Fact]
        public async Task StartIndexing_NonExistingLibrary_ShouldReturnFailure()
        {
            // Arrange
            var command = new StartIndexingCommand(999);
            _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<IndexLibraryId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IndexLibrary?)null);

            // Act
            var result = await _service.HandleAsync(command);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Contain("Index library not found");
            _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<IndexLibrary>(), It.IsAny<CancellationToken>()), Never);
        }
    }
}