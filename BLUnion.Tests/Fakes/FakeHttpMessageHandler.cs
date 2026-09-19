namespace BLUnion.Tests.Fakes;

/// <summary>
/// EIN abgefangener Request (siehe FakeHttpMessageHandler) - Body wird hier bereits als String
/// materialisiert (statt den rohen HttpContent durchzureichen), weil ein HttpContent-Stream nur
/// einmal lesbar ist und Assertions in Tests ihn ggf. mehrfach inspizieren wollen.
/// </summary>
public sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body, string? EditTokenHeader);

/// <summary>
/// Minimaler Fake-HttpMessageHandler für LiveSyncServiceTests - reicht jeden Request an einen
/// simplen Func-Delegate durch (kein externes Mocking-Framework nötig, siehe Aufgabenstellung).
/// Zeichnet jeden Request auf (URL/Body/X-Edit-Token-Header), damit Tests sowohl den Request selbst
/// als auch die Anzahl tatsächlich ausgelöster Requests prüfen können (z.B. für den
/// In-Flight-Guard: "kein zweiter Request während der erste noch läuft"). Wirft der Handler-Delegate
/// selbst, propagiert das wie ein echter Netzwerkfehler (HttpClient wandelt das in eine fehlgeschlagene
/// Task um) - damit lässt sich der "Exception im Handler"-Fehlerfall simulieren, ohne echtes Netzwerk.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        this.handler = handler;
    }

    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Erzwingt einen ECHTEN asynchronen Umschaltpunkt, bevor irgendetwas anderes passiert -
        // ohne den würde z.B. ein GET (kein Content, also kein await auf ReadAsStringAsync nötig)
        // komplett synchron bis zu einem in `handler` blockierenden Wait durchlaufen, wodurch
        // TriggerBrowse() NIE zur aufrufenden Testmethode zurückkehrt, solange dieser Aufruf läuft -
        // ein In-Flight-Guard-Test (zweiter TriggerBrowse()-Aufruf WÄHREND der erste noch läuft)
        // wäre dadurch gar nicht herstellbar. Task.Yield() garantiert dagegen immer eine echte
        // Verzögerung über den Thread-Pool, unabhängig davon, was `handler` tut.
        await Task.Yield();

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var editToken = request.Headers.TryGetValues("X-Edit-Token", out var values) ? values.FirstOrDefault() : null;

        this.Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body, editToken));

        return this.handler(request);
    }
}
