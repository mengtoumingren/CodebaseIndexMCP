using System;
using CodebaseMcpServer.Domain.IndexManagement.Aggregates;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.IndexManagement.Enums;
using CodebaseMcpServer.Domain.IndexManagement.Events;
using FluentAssertions;
using Xunit;

namespace CodebaseMcpServer.Tests.Domain.IndexManagement
{
    [Trait("Category", "Domain")]
    [Trait("TestCategory", "IndexLibrary")]
    public class IndexLibraryTests
    {
        [Fact]
        public void StartIndexing_WhenStatusIsPending_ShouldChangeStatusToIndexing()
        {
            // Arrange
            var library = CreateTestLibrary(IndexStatus.Pending);
            
            // Act
            library.StartIndexing();
            
            // Assert
            library.Status.Should().Be(IndexStatus.Indexing);
            library.DomainEvents.Should().ContainSingle()
                .Which.Should().BeOfType<IndexingStarted>();
        }
        
        [Fact]
        public void StartIndexing_WhenAlreadyIndexing_ShouldThrowException()
        {
            // Arrange
            var library = CreateTestLibrary(IndexStatus.Indexing);
            
            // Act & Assert
            library.Invoking(l => l.StartIndexing())
                .Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot start indexing in current state");
        }
        
        [Fact]
        public void CompleteIndexing_ShouldRaiseIndexingCompletedEvent()
        {
            // Arrange
            var library = CreateTestLibrary(IndexStatus.Indexing);
            var result = new IndexResult(100, 50, 0, 50, 1024L, TimeSpan.FromMinutes(5), null);
            
            // Act
            library.CompleteIndexing(result);
            
            // Assert
            library.Status.Should().Be(IndexStatus.Completed);
            library.DomainEvents.Should().ContainSingle()
                .Which.Should().BeOfType<IndexingCompleted>()
                .Which.Result.Should().Be(result);
        }
        
        private IndexLibrary CreateTestLibrary(IndexStatus status)
        {
            // For testing purposes, we need a way to create an IndexLibrary with a specific status
            // Since the constructor enforces Pending, we'll simulate the state change.
            // In a real scenario, you might use a test-specific factory or reconstitution method.
            var library = new IndexLibrary(
                new IndexLibraryId(1),
                new CodebasePath(@"C:\TestProject"),
                new CollectionName("test-collection"));

            // Manually set status for test scenario, bypassing domain rules for creation
            // This is generally not recommended for production code, but acceptable for testing internal state transitions.
            if (status == IndexStatus.Indexing)
            {
                library.StartIndexing(); // Transition to Indexing
                library.ClearDomainEvents(); // Clear events from StartIndexing for this test
            }
            else if (status == IndexStatus.Completed)
            {
                library.StartIndexing();
                library.CompleteIndexing(new IndexResult(0, 0, 0, 0, 0L, TimeSpan.Zero, null));
                library.ClearDomainEvents();
            }
            else if (status == IndexStatus.Failed)
            {
                library.StartIndexing();
                library.FailIndexing("Test failure");
                library.ClearDomainEvents();
            }
            else if (status == IndexStatus.Cancelled)
            {
                // Assuming a Cancel method exists or can be simulated
                // library.CancelIndexing();
                // library.ClearDomainEvents();
            }
            
            return library;
        }
    }
}