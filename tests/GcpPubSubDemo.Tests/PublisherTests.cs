using GcpPubSubDemo;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace GcpPubSubDemo.Tests;

public class PublisherTests
{
    [Test]
    public async Task PublishAsync_Returns_MessageId()
    {
        var settings = new PubSubSettings { ProjectId = "p", TopicId = "t", SubscriptionId = "s" };
        var mockClient = Substitute.For<PublisherServiceApiClient>();
        var expectedId = "id-123";
        mockClient.PublishAsync(Arg.Any<TopicName>(), Arg.Any<IEnumerable<PubsubMessage>>(), Arg.Any<Google.Api.Gax.Grpc.CallSettings>())
            .Returns(ci => Task.FromResult(new PublishResponse { MessageIds = { expectedId } }));

        var logger = Substitute.For<ILogger<PubSubPublisher>>();
        var publisher = new PubSubPublisher(mockClient, settings, logger);

        var id = await publisher.PublishAsync("hello");

        Assert.That(id, Is.EqualTo(expectedId));
    }
}
