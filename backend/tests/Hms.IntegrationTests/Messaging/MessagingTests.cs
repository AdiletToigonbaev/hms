using System.Net;
using System.Net.Http.Json;
using System.Text;
using Hms.IntegrationTests.Bookings;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Finance.Application;
using Hms.Modules.Guests.Application;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Messaging.Application;
using Hms.Modules.Messaging.Infrastructure;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Messaging;

/// <summary>
/// Sprint 10: уведомления (канал WhatsApp → email), история и статусы, вебхук с подписью и повторами,
/// автоответчик, ручная отправка и окно 24 ч, напоминания, права, обезличивание, код подтверждения.
/// Включает WhatsApp и меняет шаблоны гостиницы — только в группе глобальных настроек (не параллельно).
/// </summary>
[Collection(nameof(GlobalSettingsGroup))]
public class MessagingTests(PostgresFixture db)
{
    private const string PhoneNumberId = "test-phone-number-id";
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static async Task SetWhatsAppAsync(HttpClient owner, bool enabled)
    {
        var response = await owner.PutAsJsonAsync("/api/v1/settings/integrations/WhatsApp",
            new UpdateIntegrationRequest(enabled, "Sandbox", new Dictionary<string, string> { ["phoneNumberId"] = PhoneNumberId }), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task SetTemplateStatusAsync(HttpClient owner, string code, string status)
    {
        var template = (await owner.GetFromJsonAsync<List<TemplateDto>>("/api/v1/messaging/templates", Ct))!.Single(t => t.Code == code);
        var response = await owner.PutAsJsonAsync($"/api/v1/messaging/templates/{code}/ru",
            new UpdateTemplateRequest(template.EmailSubject, template.EmailBody, template.WhatsAppBody, template.WhatsAppName, status, true, template.Version), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<GuestDto> GuestAsync(HttpClient owner, string? phone, string? email, bool whatsAppConsent)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/guests", new SaveGuestRequest("Айгуль Тестовая", phone, null, email, null, null, null), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var guest = (await created.Content.ReadFromJsonAsync<GuestDto>(Ct))!;
        if (whatsAppConsent)
        {
            (await owner.PostAsJsonAsync($"/api/v1/guests/{guest.Id}/consents", new SetConsentRequest("WhatsApp", true, "FrontDesk"), Ct)).EnsureSuccessStatusCode();
        }

        return guest;
    }

    private CreateBookingRequest Booking(Guid roomId, GuestDto guest, int daysAhead, int hour = 12, bool confirm = true) =>
        new(roomId, null, null, 2, 0, guest.FullName, guest.Phone, guest.Email, null, null, null, confirm,
            Kind: StayKinds.Hourly, Date: Clock.Today.AddDays(daysAhead), StartHour: hour, Hours: 2, GuestId: guest.Id);

    private static async Task<List<MessageDto>> MessagesAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<MessageDto>>($"/api/v1/messages?{query}", Ct))!.Items.ToList();

    private async Task<HttpResponseMessage> PostWebhookAsync(string json, string? signature = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(WhatsAppSignature.Header, signature ?? WhatsAppSignature.Sign(body, PostgresFixture.WhatsAppAppSecret));
        return await _api.Anonymous().SendAsync(request, Ct);
    }

    private static string StatusPayload(string wamid, string status, string recipient) => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"waba","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp","metadata":{"display_phone_number":"996700000000","phone_number_id":"{{{PhoneNumberId}}}"},
          "statuses":[{"id":"{{{wamid}}}","status":"{{{status}}}","timestamp":"{{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}","recipient_id":"{{{recipient}}}"}]}}]}]}
        """;

    private static string InboundPayload(string from, string text, string? id = null) => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"waba","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp","metadata":{"display_phone_number":"996700000000","phone_number_id":"{{{PhoneNumberId}}}"},
          "contacts":[{"profile":{"name":"Гость WA"},"wa_id":"{{{from}}}"}],
          "messages":[{"from":"{{{from}}}","id":"{{{id ?? "wamid.in." + Guid.NewGuid().ToString("N")}}}","timestamp":"{{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}","type":"text","text":{"body":"{{{text}}}"}}]}}]}]}
        """;

    [Fact]
    public async Task ConfirmedBooking_WithoutWhatsApp_SendsEmail_AndCancellationToo()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, false);
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var email = UniqueEmail("notify");
        var guest = await GuestAsync(owner, UniquePhone(), email, whatsAppConsent: true);

        var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 40));
        await db.ProcessOutboxAsync();

        var letter = db.Emails.Sent.Last(m => m.To == email);
        letter.Subject.Should().Be($"Бронь {booking.Number} подтверждена");
        letter.TextBody.Should().Contain(booking.Number).And.Contain(room.Number).And.Contain($"https://hotel.test/account/bookings/{booking.Id}");

        (await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Планы изменились"), Ct)).EnsureSuccessStatusCode();
        await db.ProcessOutboxAsync();

        var history = await MessagesAsync(owner, $"bookingId={booking.Id}");
        history.Select(m => (m.TemplateCode, m.Channel, m.Status)).Should().BeEquivalentTo(
            [("booking_confirmed", "Email", "Sent"), ("booking_cancelled", "Email", "Sent")]);
        history.Should().OnlyContain(m => m.BookingNumber == booking.Number && m.GuestName == guest.FullName);
    }

    [Fact]
    public async Task GuestWithoutChannel_IsRecordedAsSkipped()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, false);
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var guest = await GuestAsync(owner, UniquePhone(), null, whatsAppConsent: false);

        var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 41));
        await db.ProcessOutboxAsync();

        var message = (await MessagesAsync(owner, $"bookingId={booking.Id}")).Should().ContainSingle().Subject;
        message.Status.Should().Be("Skipped");
        message.Error.Should().Contain("Нет email и согласия на WhatsApp");
    }

    [Fact]
    public async Task WhatsApp_TemplateApproved_SendsNamedParameters_StatusesFromSignedWebhook_Deduplicated()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, true);
        await SetTemplateStatusAsync(owner, "booking_confirmed", "Approved");
        try
        {
            var room = await BookingTestKit.CreateRoomAsync(owner);
            var phone = UniquePhone();
            var guest = await GuestAsync(owner, phone, UniqueEmail("wa"), whatsAppConsent: true);

            var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 42));
            await db.ProcessOutboxAsync();

            var sent = db.WhatsApp.SentTo(phone).Should().ContainSingle().Subject;
            sent.TemplateName.Should().Be("booking_confirmed");
            sent.Language.Should().Be("ru");
            sent.PhoneNumberId.Should().Be(PhoneNumberId);
            sent.Parameters.Should().Contain(p => p.Key == "booking_number" && p.Value == booking.Number);
            var message = (await MessagesAsync(owner, $"bookingId={booking.Id}")).Should().ContainSingle().Subject;
            message.Channel.Should().Be("WhatsApp");
            message.Status.Should().Be("Sent");

            // Статусы приходят вебхуком; повтор того же тела отсекается, «доставлено» после «прочитано» не откатывает.
            var wamid = await WamidAsync(message.Id);
            var read = StatusPayload(wamid, "read", phone.TrimStart('+'));
            (await PostWebhookAsync(read)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostWebhookAsync(read)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostWebhookAsync(StatusPayload(wamid, "delivered", phone.TrimStart('+')))).StatusCode.Should().Be(HttpStatusCode.OK);
            await db.ProcessOutboxAsync();

            var after = (await MessagesAsync(owner, $"bookingId={booking.Id}")).Single();
            after.Status.Should().Be("Read");
            after.ReadAt.Should().NotBeNull();
            after.DeliveredAt.Should().NotBeNull();
            await using var scope = db.Factory.Services.CreateAsyncScope();
            var events = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>().Set<Hms.Platform.Webhooks.WebhookEvent>();
            var payloads = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(events.Where(e => e.Source == "whatsapp").Select(e => e.Payload), Ct);
            payloads.Count(p => p.Contains(wamid, StringComparison.Ordinal)).Should().Be(2);
        }
        finally
        {
            await SetTemplateStatusAsync(owner, "booking_confirmed", "NotSubmitted");
            await SetWhatsAppAsync(owner, false);
        }
    }

    [Fact]
    public async Task WhatsApp_PermanentFailure_FallsBackToEmail_TemporaryFailureIsRetried()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, true);
        await SetTemplateStatusAsync(owner, "booking_confirmed", "Approved");
        try
        {
            var room = await BookingTestKit.CreateRoomAsync(owner);

            // Окончательная ошибка (нет WhatsApp у номера) → письмо-замена.
            var phone = UniquePhone();
            var email = UniqueEmail("fallback");
            db.WhatsApp.FailFor(phone, 131026, retryable: false);
            var guest = await GuestAsync(owner, phone, email, whatsAppConsent: true);
            var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 43));
            await db.ProcessOutboxAsync();

            var history = await MessagesAsync(owner, $"bookingId={booking.Id}");
            history.Should().HaveCount(2);
            var failed = history.Single(m => m.Channel == "WhatsApp");
            failed.Status.Should().Be("Failed");
            failed.Error.Should().Contain("нет WhatsApp");
            failed.CanRetry.Should().BeTrue();
            history.Single(m => m.Channel == "Email").FallbackFor.Should().Be(failed.Id);
            db.Emails.Sent.Should().Contain(m => m.To == email && m.Subject == $"Бронь {booking.Number} подтверждена");

            // Временная ошибка (лимит Meta) → сообщение ждёт повтора, письма нет; после снятия ошибки — отправлено.
            var phone2 = UniquePhone();
            db.WhatsApp.FailFor(phone2, 130429, retryable: true);
            var guest2 = await GuestAsync(owner, phone2, UniqueEmail("retry"), whatsAppConsent: true);
            var booking2 = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest2, 44));
            await db.ProcessOutboxAsync();
            var pending = (await MessagesAsync(owner, $"bookingId={booking2.Id}")).Should().ContainSingle().Subject;
            pending.Status.Should().Be("Queued");
            pending.Error.Should().NotBeNullOrEmpty();

            db.WhatsApp.Succeed(phone2);
            await ForceRetryAsync();
            await db.ProcessOutboxAsync();
            (await MessagesAsync(owner, $"bookingId={booking2.Id}")).Single().Status.Should().Be("Sent");
        }
        finally
        {
            await SetTemplateStatusAsync(owner, "booking_confirmed", "NotSubmitted");
            await SetWhatsAppAsync(owner, false);
        }
    }

    [Fact]
    public async Task Webhook_RejectsBadSignature_AndVerifiesChallenge()
    {
        var bad = await PostWebhookAsync(InboundPayload("996700000001", "привет"), "sha256=" + new string('0', 64));
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var client = _api.Anonymous();
        var ok = await client.GetAsync(new Uri($"/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={PostgresFixture.WhatsAppVerifyToken}&hub.challenge=12345", UriKind.Relative), Ct);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync(Ct)).Should().Be("12345");
        var wrong = await client.GetAsync(new Uri("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1", UriKind.Relative), Ct);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Inbound_AutoReplies_Menu_Prices_Operator_Stop_AndManualReplyInWindow()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, true);
        try
        {
            var phone = UniquePhone();
            var guest = await GuestAsync(owner, phone, UniqueEmail("chat"), whatsAppConsent: true);
            var from = phone.TrimStart('+');

            // Первое обращение — приветствие с меню.
            (await PostWebhookAsync(InboundPayload(from, "Здравствуйте"))).EnsureSuccessStatusCode();
            await db.ProcessOutboxAsync();
            db.WhatsApp.SentTo(phone)[^1].Text.Should().Contain("1 — Номера").And.Contain("2 — Цены");

            // Ключевые слова: «сколько стоит» → цены.
            (await PostWebhookAsync(InboundPayload(from, "Сколько стоит номер?"))).EnsureSuccessStatusCode();
            await db.ProcessOutboxAsync();
            db.WhatsApp.SentTo(phone)[^1].Text.Should().StartWith("Цены:");

            var conversation = (await owner.GetFromJsonAsync<PagedResult<ConversationDto>>($"/api/v1/whatsapp/conversations?search={from[^6..]}", Ct))!.Items.Single();
            conversation.GuestId.Should().Be(guest.Id);
            conversation.WindowOpen.Should().BeTrue();

            // «Администратор» → диалог у сотрудника, автоответчик молчит.
            (await PostWebhookAsync(InboundPayload(from, "позовите администратора"))).EnsureSuccessStatusCode();
            await db.ProcessOutboxAsync();
            var sentBefore = db.WhatsApp.SentTo(phone).Count;
            (await PostWebhookAsync(InboundPayload(from, "Можно поздний выезд?"))).EnsureSuccessStatusCode();
            await db.ProcessOutboxAsync();
            db.WhatsApp.SentTo(phone).Count.Should().Be(sentBefore);
            (await owner.GetFromJsonAsync<ConversationDto>($"/api/v1/whatsapp/conversations/{conversation.Id}", Ct))!.NeedsOperator.Should().BeTrue();

            // Ответ администратора в окне — свободным текстом.
            using (var send = new HttpRequestMessage(HttpMethod.Post, "/api/v1/messages")
            {
                Content = JsonContent.Create(new SendMessageRequest(null, null, conversation.Id, "WhatsApp", "Да, до 14:00 без доплаты.", null)),
            })
            {
                send.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                var response = await owner.SendAsync(send, Ct);
                response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            }

            await db.ProcessOutboxAsync();
            db.WhatsApp.SentTo(phone)[^1].Text.Should().Be("Да, до 14:00 без доплаты.");
            (await owner.PostAsync(new Uri($"/api/v1/whatsapp/conversations/{conversation.Id}/resolve", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();

            // «Стоп» — согласие на WhatsApp отозвано, ответ об отписке.
            (await PostWebhookAsync(InboundPayload(from, "СТОП"))).EnsureSuccessStatusCode();
            await db.ProcessOutboxAsync();
            db.WhatsApp.SentTo(phone)[^1].Text.Should().Contain("отписались");
            var consents = (await owner.GetFromJsonAsync<List<ConsentHistoryDto>>($"/api/v1/guests/{guest.Id}/consents", Ct))!;
            consents.OrderByDescending(c => c.RecordedAt).First(c => c.Type == "WhatsApp").Granted.Should().BeFalse();

            var history = await MessagesAsync(owner, $"conversationId={conversation.Id}&pageSize=100");
            history.Count(m => m.Direction == "In").Should().Be(5);
            history.Should().Contain(m => m.Kind == "Manual" && m.SentByName != null);
        }
        finally
        {
            await SetWhatsAppAsync(owner, false);
        }
    }

    [Fact]
    public async Task ManualWhatsApp_OutsideWindow_NeedsApprovedTemplate_EmailWorks()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, true);
        try
        {
            var email = UniqueEmail("manual");
            var guest = await GuestAsync(owner, UniquePhone(), email, whatsAppConsent: true);

            var options = (await owner.GetFromJsonAsync<SendOptionsDto>($"/api/v1/messages/send-options?guestId={guest.Id}", Ct))!;
            options.WindowOpen.Should().BeFalse();
            options.WhatsAppAvailable.Should().BeFalse();
            options.WhatsAppBlockReason.Should().Contain("не одобрен");

            var whatsApp = await SendAsync(owner, new SendMessageRequest(guest.Id, null, null, "WhatsApp", "Номер готов", null));
            whatsApp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(whatsApp)).Should().Be("whatsapp.outside_window");

            var letter = await SendAsync(owner, new SendMessageRequest(guest.Id, null, null, "Email", "Номер готов, ждём вас.", "Ваш номер готов"));
            letter.StatusCode.Should().Be(HttpStatusCode.Created);
            await db.ProcessOutboxAsync();
            var sent = db.Emails.Sent.Last(m => m.To == email);
            sent.Subject.Should().Be("Ваш номер готов");
            sent.TextBody.Should().Contain("Номер готов, ждём вас.");
        }
        finally
        {
            await SetWhatsAppAsync(owner, false);
        }
    }

    [Fact]
    public async Task Reminders_AreSentOnce_AtConfiguredTime()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, false);
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var email = UniqueEmail("remind");
        var guest = await GuestAsync(owner, UniquePhone(), email, whatsAppConsent: false);
        var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 3, hour: 15));
        var startsAt = Clock.ToUtc(Clock.Today.AddDays(3), new TimeOnly(15, 0));

        await using var scope = db.Factory.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IReminderScheduler>();
        // За 61 минуту — рано (по умолчанию за 60 мин до начала почасовой брони); за 30 минут — пора, повторный проход — без дубля.
        (await scheduler.RunAsync(startsAt.AddMinutes(-61), Ct)).CheckIn.Should().Be(0);
        (await scheduler.RunAsync(startsAt.AddMinutes(-30), Ct)).CheckIn.Should().BeGreaterThanOrEqualTo(1);
        await using (var again = db.Factory.Services.CreateAsyncScope())
        {
            await again.ServiceProvider.GetRequiredService<IReminderScheduler>().RunAsync(startsAt.AddMinutes(-20), Ct);
        }

        await db.ProcessOutboxAsync();
        var reminders = (await MessagesAsync(owner, $"bookingId={booking.Id}")).Where(m => m.TemplateCode == "checkin_reminder").ToList();
        reminders.Should().ContainSingle().Which.Status.Should().Be("Sent");
        db.Emails.Sent.Should().Contain(m => m.To == email && m.Subject == $"Скоро заезд: бронь {booking.Number}");
    }

    [Fact]
    public async Task Permissions_MaidCannotReadMessages_AdminCannotEditTemplates()
    {
        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        (await maid.GetAsync(new Uri("/api/v1/messages", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.GetAsync(new Uri("/api/v1/whatsapp/conversations", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        (await admin.GetAsync(new Uri("/api/v1/messages", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = (await admin.GetFromJsonAsync<List<TemplateDto>>("/api/v1/messaging/templates", Ct))!;
        var t = templates.First();
        (await admin.PutAsJsonAsync($"/api/v1/messaging/templates/{t.Code}/ru",
            new UpdateTemplateRequest(t.EmailSubject, t.EmailBody, t.WhatsAppBody, t.WhatsAppName, t.WhatsAppStatus, t.IsActive, t.Version), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(new Uri("/api/v1/messaging/settings", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Template_UnknownVariable_IsRejected_PreviewUsesSampleData()
    {
        var owner = await OwnerAsync();
        var t = (await owner.GetFromJsonAsync<List<TemplateDto>>("/api/v1/messaging/templates", Ct))!.Single(x => x.Code == "payment_received");
        var bad = await owner.PutAsJsonAsync("/api/v1/messaging/templates/payment_received/ru",
            new UpdateTemplateRequest(t.EmailSubject, t.EmailBody + " {{passport}}", t.WhatsAppBody, t.WhatsAppName, t.WhatsAppStatus, t.IsActive, t.Version), Ct);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(bad)).Should().Be("template.unknown_variable");

        var preview = (await owner.GetFromJsonAsync<TemplatePreviewDto>("/api/v1/messaging/templates/payment_received/ru/preview", Ct))!;
        preview.EmailBody.Should().Contain("1 000 сом").And.Contain("Ч-2811-01");
    }

    [Fact]
    public async Task PaymentReceived_Notifies_AndAnonymizationErasesMessages()
    {
        var owner = await OwnerAsync();
        await SetWhatsAppAsync(owner, false);
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var email = UniqueEmail("pay");
        var guest = await GuestAsync(owner, UniquePhone(), email, whatsAppConsent: false);
        var booking = await BookingTestKit.CreateAsync(owner, Booking(room.Id, guest, 45));

        using (var pay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/manual")
        {
            Content = JsonContent.Create(new RegisterPaymentRequest(booking.Id, "Prepayment", "Cash", 300m, null)),
        })
        {
            pay.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            (await owner.SendAsync(pay, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await db.ProcessOutboxAsync();
        db.Emails.Sent.Should().Contain(m => m.To == email && m.Subject == $"Оплата по брони {booking.Number} получена" && m.TextBody.Contains("300 сом", StringComparison.Ordinal));

        (await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Тест"), Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsync(new Uri($"/api/v1/guests/{guest.Id}/anonymize", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await MessagesAsync(owner, $"guestId={guest.Id}");
        history.Should().NotBeEmpty().And.OnlyContain(m => m.Recipient == null && m.Body == "[данные гостя удалены]");
    }

    [Fact]
    public async Task Registration_ByWhatsAppCode_ConfirmsPhone_AndAllowsLogin()
    {
        var owner = await OwnerAsync();
        var anonymous = _api.Anonymous();
        var phone = UniquePhone();
        var email = UniqueEmail("code");
        var request = new RegisterRequest("Гость Код", email, phone, GuestPassword, true, true, ConfirmBy: "WhatsApp");

        // WhatsApp выключен — регистрировать кодом нельзя.
        await SetWhatsAppAsync(owner, false);
        (await anonymous.GetFromJsonAsync<ConfirmationChannelsDto>("/api/v1/auth/confirmation-channels", Ct))!.WhatsApp.Should().BeFalse();
        var unavailable = await anonymous.PostAsJsonAsync("/api/v1/auth/register", request, Ct);
        (await ProblemCodeAsync(unavailable)).Should().Be("whatsapp.unavailable");

        await SetWhatsAppAsync(owner, true);
        await SetTemplateStatusAsync(owner, "verification_code", "Approved");
        try
        {
            (await anonymous.GetFromJsonAsync<ConfirmationChannelsDto>("/api/v1/auth/confirmation-channels", Ct))!.WhatsApp.Should().BeTrue();
            var registered = await anonymous.PostAsJsonAsync("/api/v1/auth/register", request, Ct);
            registered.StatusCode.Should().Be(HttpStatusCode.Created, await registered.Content.ReadAsStringAsync(Ct));
            var response = (await registered.Content.ReadFromJsonAsync<RegisterResponse>(Ct))!;
            response.ConfirmationChannel.Should().Be("WhatsApp");
            await db.ProcessOutboxAsync();

            var sent = db.WhatsApp.SentTo(phone).Should().ContainSingle().Subject;
            sent.IsAuthentication.Should().BeTrue();
            var code = sent.Parameters.Single().Value;
            code.Should().MatchRegex("^[0-9]{6}$");

            (await _api.LoginRawAsync(email, GuestPassword)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var wrong = await anonymous.PostAsJsonAsync("/api/v1/auth/confirm-phone", new ConfirmPhoneRequest(response.UserId, code == "000000" ? "111111" : "000000"), Ct);
            (await ProblemCodeAsync(wrong)).Should().Be("auth.invalid_code");
            (await anonymous.PostAsJsonAsync("/api/v1/auth/confirm-phone", new ConfirmPhoneRequest(response.UserId, code), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await _api.LoginRawAsync(email, GuestPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Код в истории не хранится.
            var history = await MessagesAsync(owner, "kind=VerificationCode&pageSize=100");
            history.Should().NotContain(m => m.Body.Contains(code, StringComparison.Ordinal));
        }
        finally
        {
            await SetTemplateStatusAsync(owner, "verification_code", "NotSubmitted");
            await SetWhatsAppAsync(owner, false);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, SendMessageRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/messages") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private async Task<string> WamidAsync(Guid messageId)
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(ctx.Set<Hms.Modules.Messaging.Domain.Message>())
                .Where(m => m.Id == messageId).Select(m => m.ExternalId!), Ct);
    }

    /// <summary>Повтор outbox после паузы: сдвигаем срок следующей попытки (в тестах время не ждём).</summary>
    private async Task ForceRetryAsync()
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>();
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(ctx.Database,
            "UPDATE platform.outbox_messages SET next_attempt_at = now() - interval '1 second' WHERE status = 'Pending'", Ct);
    }
}
