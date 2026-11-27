using System.Text;
using System.Text.Json;
using Hms.Modules.Messaging.Domain;
using Hms.Modules.Messaging.Infrastructure;

namespace Hms.UnitTests.Messaging;

public class TemplateTextTests
{
    [Fact]
    public void Render_ReplacesVariables_AndCollapsesEmptyLines()
    {
        var text = "Здравствуйте, {{guest_name}}!\n\n{{cancel_info}}\n\n\n{{hotel_name}}";
        var result = TemplateText.Render(text, new Dictionary<string, string> { ["guest_name"] = "Айгуль", ["hotel_name"] = "Пример", ["cancel_info"] = string.Empty });

        result.Should().Be("Здравствуйте, Айгуль!\n\nПример");
    }

    [Fact]
    public void Validate_RejectsUnknownVariables_AndBrokenBraces()
    {
        string[] allowed = ["guest_name"];
        TemplateText.Validate("Привет, {{guest_name}}", allowed, "Текст").Should().BeNull();
        TemplateText.Validate("Паспорт: {{passport}}", allowed, "Текст")!.Code.Should().Be("template.unknown_variable");
        TemplateText.Validate("Привет, {{guest_name}", allowed, "Текст")!.Code.Should().Be("template.bad_placeholder");
    }

    [Fact]
    public void Placeholders_InOrder_WithoutDuplicates() =>
        TemplateText.Placeholders("{{b}} {{a}} {{b}}").Should().Equal("b", "a");

    [Fact]
    public void ToHtml_EncodesText_AndLinksUrls()
    {
        var html = TemplateText.ToHtml("<b>Бронь</b>\nСсылка: https://example.com/a");
        html.Should().Contain("&lt;b&gt;Бронь&lt;/b&gt;<br>").And.Contain("<a href=\"https://example.com/a\" style=");
    }

    [Fact]
    public void Catalog_DefaultTexts_UseOnlyDeclaredVariables()
    {
        foreach (var d in TemplateCatalog.All)
        {
            TemplateText.Validate(d.DefaultSubject, d.Variables, d.Code).Should().BeNull(d.Code);
            TemplateText.Validate(d.DefaultBody, d.Variables, d.Code).Should().BeNull(d.Code);
            TemplateText.Validate(d.DefaultWhatsAppBody, d.Variables, d.Code).Should().BeNull(d.Code);
            d.DefaultWhatsAppBody.Length.Should().BeLessThanOrEqualTo(1024, d.Code);
        }

        foreach (var item in AutoReply.Defaults(Guid.NewGuid()))
        {
            TemplateText.Validate(item.Body, AutoReplyVariables.All, item.Title).Should().BeNull(item.Title);
        }
    }
}

public class AutoReplyMatcherTests
{
    private static readonly IReadOnlyList<AutoReply> Items = AutoReply.Defaults(Guid.NewGuid());

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 5 ", 5)]
    [InlineData("Сколько стоит номер?", 2)]
    [InlineData("Есть свободные номера на выходные", 1)]
    [InlineData("Во сколько заезд?", 3)]
    [InlineData("позовите администратора", 7)]
    public void Match_ByNumberOrKeywords(string text, int expected) =>
        AutoReplyMatcher.Match(text, Items)!.Number.Should().Be(expected);

    [Fact]
    public void Match_Unknown_ReturnsNull() => AutoReplyMatcher.Match("абракадабра", Items).Should().BeNull();

    [Theory]
    [InlineData("СТОП")]
    [InlineData("stop")]
    [InlineData("Отписаться!")]
    public void IsStop(string text) => AutoReplyMatcher.IsStop(text).Should().BeTrue();

    [Fact]
    public void Menu_ListsActiveItemsInOrder() =>
        AutoReplyMatcher.Menu(Items).Split('\n')[0].Should().Be("1 — Номера");
}

public class ReminderRulesTests
{
    private static readonly TimeZoneInfo Bishkek = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bishkek");
    private static readonly MessagingSettings Defaults = MessagingSettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Hourly_Reminders_60MinutesBeforeStart_15BeforeEnd()
    {
        var start = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        ReminderRules.StartReminderAt(Defaults, hourly: true, start, Bishkek).Should().Be(start.AddMinutes(-60));
        ReminderRules.EndReminderAt(Defaults, hourly: true, start.AddHours(3)).Should().Be(start.AddHours(3).AddMinutes(-15));
    }

    [Fact]
    public void Nightly_StartReminder_DayBeforeAt18_HotelTime()
    {
        // Заезд 2 декабря 14:00 по Бишкеку (UTC+6) = 08:00 UTC → напоминание 1 декабря 18:00 по Бишкеку = 12:00 UTC.
        var start = new DateTimeOffset(2026, 12, 2, 8, 0, 0, TimeSpan.Zero);
        ReminderRules.StartReminderAt(Defaults, hourly: false, start, Bishkek).UtcDateTime.Should().Be(new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc));
        ReminderRules.EndReminderAt(Defaults, hourly: false, start.AddDays(1)).Should().Be(start.AddDays(1).AddMinutes(-60));
    }

    [Fact]
    public void IsDue_OnlyBetweenReminderAndDeadline_AndNotForFreshBookings()
    {
        var at = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
        var deadline = at.AddHours(1);
        ReminderRules.IsDue(at, deadline, at.AddDays(-1), at.AddMinutes(-1)).Should().BeFalse();
        ReminderRules.IsDue(at, deadline, at.AddDays(-1), at.AddMinutes(10)).Should().BeTrue();
        ReminderRules.IsDue(at, deadline, at.AddDays(-1), deadline).Should().BeFalse();
        ReminderRules.IsDue(at, deadline, at.AddMinutes(5), at.AddMinutes(10)).Should().BeFalse();
    }
}

public class MessageStatusTests
{
    private static Message Text() =>
        Message.WhatsAppText(Guid.NewGuid(), null, null, MessageKind.Manual, "+996700000001", "текст", null, DateTimeOffset.UtcNow);

    [Fact]
    public void Status_OnlyGrows_OutOfOrderWebhooksDoNotRollBack()
    {
        var m = Text();
        m.MarkSent("wamid.1", DateTimeOffset.UtcNow);
        m.ApplyDeliveryStatus(MessageStatus.Read, DateTimeOffset.UtcNow, null).Should().BeTrue();
        m.ApplyDeliveryStatus(MessageStatus.Delivered, DateTimeOffset.UtcNow, null).Should().BeFalse();
        m.ApplyDeliveryStatus(MessageStatus.Failed, DateTimeOffset.UtcNow, "x").Should().BeFalse();
        m.Status.Should().Be(MessageStatus.Read);
        m.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void TemporaryFailures_EndInFailed_AfterMaxAttempts_ThenManualRetry()
    {
        var m = Text();
        for (var i = 1; i < Message.MaxAttempts; i++)
        {
            m.RecordTemporaryFailure("лимит", DateTimeOffset.UtcNow).Should().BeFalse();
        }

        m.RecordTemporaryFailure("лимит", DateTimeOffset.UtcNow).Should().BeTrue();
        m.Status.Should().Be(MessageStatus.Failed);
        m.Retry(DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        m.Status.Should().Be(MessageStatus.Queued);
    }

    [Fact]
    public void ServiceWindow_Is24HoursAfterLastInbound()
    {
        var now = DateTimeOffset.UtcNow;
        var c = Conversation.Start(Guid.NewGuid(), "+996700000001", null, now);
        c.IsWindowOpen(now).Should().BeFalse();
        c.RegisterInbound(null, "Гость", now).Should().BeTrue();
        c.RegisterInbound(null, null, now.AddHours(1)).Should().BeFalse();
        c.IsWindowOpen(now.AddHours(24)).Should().BeTrue();
        c.IsWindowOpen(now.AddHours(25)).Should().BeFalse();
    }
}

public class WhatsAppProtocolTests
{
    [Fact]
    public void Signature_HmacSha256OfRawBody()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var header = WhatsAppSignature.Sign(body, "secret");
        header.Should().StartWith("sha256=");
        WhatsAppSignature.IsValid(body, header, "secret").Should().BeTrue();
        WhatsAppSignature.IsValid(body, header, "other").Should().BeFalse();
        WhatsAppSignature.IsValid(Encoding.UTF8.GetBytes("{\"a\":2}"), header, "secret").Should().BeFalse();
        WhatsAppSignature.IsValid(body, "sha256=zz", "secret").Should().BeFalse();
        WhatsAppSignature.IsValid(body, null, "secret").Should().BeFalse();
    }

    [Theory]
    [InlineData(130429, true)]
    [InlineData(131056, true)]
    [InlineData(131000, true)]
    [InlineData(131047, false)]
    [InlineData(131026, false)]
    [InlineData(132001, false)]
    [InlineData(190, false)]
    public void ErrorCodes_RetryableOnlyForLimitsAndOutages(int code, bool retryable) =>
        WhatsAppErrorCodes.IsRetryable(code).Should().Be(retryable);

    [Fact]
    public void Body_NamedTemplate_FollowsMetaFormat()
    {
        using var doc = Serialize(new WhatsAppOutgoing("pn", "+996700000001", "booking_confirmed", "ru",
            [new("guest_name", "Айгуль"), new("booking_number", "Ч-1")], false, null));
        var root = doc.RootElement;
        root.GetProperty("messaging_product").GetString().Should().Be("whatsapp");
        root.GetProperty("to").GetString().Should().Be("+996700000001");
        root.GetProperty("type").GetString().Should().Be("template");
        var template = root.GetProperty("template");
        template.GetProperty("language").GetProperty("code").GetString().Should().Be("ru");
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        parameters[0].GetProperty("parameter_name").GetString().Should().Be("guest_name");
        parameters[1].GetProperty("text").GetString().Should().Be("Ч-1");
    }

    [Fact]
    public void Body_AuthenticationTemplate_CodeInBodyAndCopyCodeButton()
    {
        using var doc = Serialize(new WhatsAppOutgoing("pn", "+996700000001", "verification_code", "ru", [new("code", "123456")], true, null));
        var components = doc.RootElement.GetProperty("template").GetProperty("components");
        components[0].GetProperty("type").GetString().Should().Be("body");
        components[0].GetProperty("parameters")[0].TryGetProperty("parameter_name", out _).Should().BeFalse();
        components[1].GetProperty("type").GetString().Should().Be("button");
        components[1].GetProperty("sub_type").GetString().Should().Be("url");
        components[1].GetProperty("index").GetString().Should().Be("0");
        components[1].GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be("123456");
    }

    [Fact]
    public void Body_FreeText()
    {
        using var doc = Serialize(new WhatsAppOutgoing("pn", "+996700000001", null, null, [], false, "Привет"));
        doc.RootElement.GetProperty("type").GetString().Should().Be("text");
        doc.RootElement.GetProperty("text").GetProperty("body").GetString().Should().Be("Привет");
    }

    [Fact]
    public void ReadError_ParsesGraphApiError()
    {
        var (code, message) = CloudApiWhatsAppClient.ReadError(
            """{"error":{"message":"(#131047) Re-engagement message","type":"OAuthException","code":131047,"error_data":{"messaging_product":"whatsapp","details":"Message failed to send because more than 24 hours have passed"},"fbtrace_id":"x"}}""");
        code.Should().Be(131047);
        message.Should().StartWith("Message failed to send");
    }

    [Fact]
    public void Parser_ReadsInboundText_MediaPlaceholder_StatusesAndErrors()
    {
        const string json = """
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp","metadata":{"display_phone_number":"1","phone_number_id":"PN"},
              "contacts":[{"profile":{"name":"Kerry"},"wa_id":"16505551234"}],
              "messages":[{"from":"16505551234","id":"wamid.in","timestamp":"1700000000","type":"text","text":{"body":"Hi"}},
                          {"from":"16505551234","id":"wamid.img","timestamp":"1700000001","type":"image","image":{}}],
              "statuses":[{"id":"wamid.out","status":"failed","timestamp":"1700000002","recipient_id":"16505551234",
                           "errors":[{"code":131026,"title":"Message undeliverable","error_data":{"details":"x"}}]}]}}]}]}
            """;
        var batch = WhatsAppWebhookParser.Parse(json).Should().ContainSingle().Subject;
        batch.PhoneNumberId.Should().Be("PN");
        batch.Messages.Should().HaveCount(2);
        batch.Messages[0].Text.Should().Be("Hi");
        batch.Messages[0].ProfileName.Should().Be("Kerry");
        batch.Messages[1].Text.Should().Be("[фото]");
        var status = batch.Statuses.Should().ContainSingle().Subject;
        status.Status.Should().Be(MessageStatus.Failed);
        status.ErrorCode.Should().Be(131026);
    }

    private static JsonDocument Serialize(WhatsAppOutgoing message) =>
        JsonDocument.Parse(JsonSerializer.Serialize(CloudApiWhatsAppClient.BuildBody(message)));
}
