using System.Text.Json;
using Xunit.Abstractions;
using Souqly.Messaging;

namespace Saga.IntegrationTests;

/// <summary>Saga sur le broker en mémoire : rapide, sans Docker, avec pannes simulées.</summary>
[Trait("Category", "Integration")]
public sealed class InMemorySagaTests(SagaFixture platform, ITestOutputHelper output) : SagaScenarios(platform), IClassFixture<SagaFixture>
{
    [Fact]
    public async Task Livraison_en_double_de_chaque_message_effet_unique_un_debit_une_sortie_de_stock_une_notification()
    {
        Platform.Broker.DeliverTwice = true;   // chaque message arrive deux fois (consommateur redémarré avant le commit)
        try
        {
            var tapis = await NewProductAsync("vendor-fatima", "Tapis double", 1000m, stock: 6);
            var customer = NewCustomer();

            var order = await PlaceAsync(customer, (tapis, 2));
            await WaitForStatusAsync(customer, order.Id, "CONFIRMED");
            await Platform.Broker.WaitUntilIdleAsync(TimeSpan.FromSeconds(20));

            (await EventuallyAsync(() => StockAsync("vendor-fatima", tapis), s => s.Reserved == 0)).OnHand.ShouldBe(4);   // 6 - 2, pas 6 - 4
            (await PaymentAsync(customer, order.Id)).Amount.ShouldBe("2000.00");
            (await EventuallyAsync(() => NotificationsAsync(customer), n => n.Any(x => x.OrderId == order.Id))).Count(n => n.OrderId == order.Id).ShouldBe(1);
            Platform.Broker.Published.Count(m => m.Message.Type == "PaymentSucceeded" && m.Message.Key == order.Id.ToString()).ShouldBe(1);
        }
        finally
        {
            Platform.Broker.DeliverTwice = false;
        }
    }

    [Fact]
    public async Task Message_illisible_envoye_en_DLQ_apres_3_essais_sans_bloquer_les_commandes_suivantes()
    {
        var poison = new MessageEnvelope(Guid.CreateVersion7(), "OrderPlaced", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "{ pas du json", null);
        Platform.Broker.Publish(Topics.OrderingEvents, poison);

        // La commande suivante passe malgré le message empoisonné devant elle.
        var tapis = await NewProductAsync("vendor-fatima", "Tapis après panne", 500m, stock: 2);
        var customer = NewCustomer();
        var order = await PlaceAsync(customer, (tapis, 1));
        await WaitForStatusAsync(customer, order.Id, "CONFIRMED");

        var dead = await EventuallyAsync(
            () => Task.FromResult(Platform.Broker.Published.Where(m => m.Topic == Topics.DeadLetter(Topics.OrderingEvents)).ToList()),
            d => d.Any(m => m.Message.Id == poison.Id));
        dead.Count(m => m.Message.Id == poison.Id).ShouldBe(1);   // Inventory seul a un gestionnaire OrderPlaced
    }

    [Fact]
    public async Task US19_une_seule_trace_suit_la_commande_de_la_requete_HTTP_jusqu_a_la_confirmation()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis tracé", 300m, stock: 1);
        var customer = NewCustomer();
        var order = await PlaceAsync(customer, (tapis, 1));

        var chain = await EventuallyAsync(
            () => Task.FromResult(Platform.Broker.Published.Where(m => m.Message.Key == order.Id.ToString()).Select(m => m.Message).ToList()),
            c => c.Any(m => m.Type == "OrderConfirmed"));

        foreach (var (topic, message) in Platform.Broker.Published.Where(m => m.Message.Key == order.Id.ToString()))
        {
            output.WriteLine($"{message.OccurredAt:HH:mm:ss.fff}  {topic,-35} {message.Type,-17} trace={message.TraceParent?.Split('-')[1][..12]}…  {message.Payload}");
        }

        chain.Select(m => m.Type).ShouldBe(["OrderPlaced", "StockReserved", "PaymentRequested", "PaymentSucceeded", "OrderConfirmed"]);
        // traceparent = 00-<trace-id>-<span-id>-01 : les cinq messages, produits par trois services, ont le même trace-id.
        chain.Select(m => m.TraceParent!.Split('-')[1]).Distinct().Count().ShouldBe(1);
        JsonDocument.Parse(chain[0].Payload).RootElement.GetProperty("total").GetDecimal().ShouldBe(300m);
    }
}
