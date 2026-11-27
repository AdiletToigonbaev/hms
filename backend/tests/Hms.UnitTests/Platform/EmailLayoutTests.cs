using Hms.Platform.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.UnitTests.Platform;

public class EmailLayoutTests
{
    private static readonly EmailBrand Brand = new("Great Hotel", "г. Бишкек, ул. Примерная, 1", "+996 555 000 000", "https://hotel.example.com");

    [Fact]
    public void Wrap_HasHotelName_Contacts_AndContent()
    {
        var html = EmailLayout.Wrap(Brand, "Подтверждение email", "<p>Содержимое</p>");

        html.Should().StartWith("<!doctype html>")
            .And.Contain("<title>Подтверждение email</title>")
            .And.Contain(">G</span>")
            .And.Contain("Great Hotel")
            .And.Contain("г. Бишкек, ул. Примерная, 1")
            .And.Contain("+996 555 000 000")
            .And.Contain(">hotel.example.com</a>")
            .And.Contain("<p>Содержимое</p>")
            .And.Contain("#b5562f");
    }

    [Fact]
    public void Wrap_EncodesHotelData()
    {
        // Название и контакты вводит владелец — в письмо они попадают только экранированными.
        var html = EmailLayout.Wrap(new EmailBrand("<script>x</script>", "<b>адрес</b>", null, "javascript:alert(1)"), "Тема", "");

        html.Should().NotContain("<script>").And.NotContain("<b>адрес</b>").And.NotContain("javascript:");
        html.Should().Contain("&lt;script&gt;x&lt;/script&gt;");
    }

    [Fact]
    public void Wrap_WithoutOptionalContacts_ShowsOnlyName()
    {
        var html = EmailLayout.Wrap(new EmailBrand("Great Hotel", null, null, null), "Тема", "<p>x</p>");

        html.Should().Contain("Great Hotel").And.NotContain(" · ");
    }

    [Fact]
    public void Button_EncodesLink()
    {
        EmailLayout.Button("https://hotel.example.com/a?x=1&y=\"2\"", "Подтвердить")
            .Should().Contain("href=\"https://hotel.example.com/a?x=1&amp;y=&quot;2&quot;\"")
            .And.Contain(">Подтвердить</a>");
    }

    [Fact]
    public void WrapText_AppendsContacts()
    {
        EmailLayout.WrapText(Brand, "Текст письма\n").Should()
            .Be("Текст письма\n\n—\nGreat Hotel\nг. Бишкек, ул. Примерная, 1\n+996 555 000 000\nhttps://hotel.example.com\n");
    }

    [Fact]
    public async Task Composer_FromName_IsHotelName_UnlessConfigured()
    {
        var provider = new StubBrandProvider(Brand);
        var content = new EmailMessage("guest@example.com", "Тема", "<p>x</p>", "x");

        var byHotel = await new EmailComposerProbe(provider, fromName: null).ComposeAsync(content);
        var configured = await new EmailComposerProbe(provider, fromName: "Ресепшен").ComposeAsync(content);

        byHotel.FromName.Should().Be("Great Hotel");
        byHotel.HtmlBody.Should().Contain("Great Hotel").And.Contain("<p>x</p>");
        configured.FromName.Should().Be("Ресепшен");
    }

    private sealed class StubBrandProvider(EmailBrand brand) : IEmailBrandProvider
    {
        public Task<EmailBrand> GetAsync(CancellationToken ct) => Task.FromResult(brand);
    }

    /// <summary>EmailComposer — internal; проверяем его через DI, как в приложении.</summary>
    private sealed class EmailComposerProbe(IEmailBrandProvider provider, string? fromName)
    {
        public async Task<EmailMessage> ComposeAsync(EmailMessage content)
        {
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:FromName"] = fromName })
                .Build();
            services.AddLogging();
            services.AddPlatform(config);
            services.AddScoped(_ => provider);
            await using var sp = services.BuildServiceProvider();
            await using var scope = sp.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IEmailComposer>().ComposeAsync(content, CancellationToken.None);
        }
    }
}
