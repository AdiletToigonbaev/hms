using Hms.Modules.Guests.Contracts;
using Hms.Modules.Guests.Domain;
using Hms.SharedKernel;

namespace Hms.UnitTests.Guests;

public class GuestDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static Guest NewGuest() =>
        Guest.Create(Guid.NewGuid(), GuestSource.Admin, "  Айгуль  ", PhoneNumber.Parse("0555 123 456").Value, " AIGUL@Example.COM ", Now);

    [Fact]
    public void Create_NormalizesContacts()
    {
        var guest = NewGuest();

        guest.FullName.Should().Be("Айгуль");
        guest.Phone.Should().Be("+996555123456");
        guest.Email.Should().Be("aigul@example.com");
    }

    [Fact]
    public void Update_ReturnsChangedFieldNames_ForAuditWithoutValues()
    {
        var guest = NewGuest();

        var changed = guest.Update("Айгуль", PhoneNumber.Parse("+996555123456").Value, PhoneNumber.Parse("+996700000001").Value, "aigul@example.com", null, " заметка ", Now.AddHours(1));

        changed.Should().BeEquivalentTo(["whatsAppPhone", "notes"]);
        guest.Notes.Should().Be("заметка");
        guest.UpdatedAt.Should().Be(Now.AddHours(1));
        guest.Update("Айгуль", PhoneNumber.Parse("+996555123456").Value, PhoneNumber.Parse("+996700000001").Value, "aigul@example.com", null, "заметка", Now.AddHours(2))
            .Should().BeEmpty();
    }

    [Fact]
    public void Consents_KeepHistory_AndSkipRepeatedState()
    {
        var guest = NewGuest();

        guest.RecordConsent(ConsentType.WhatsApp, true, ConsentSource.FrontDesk, null, Now).Should().BeTrue();
        guest.RecordConsent(ConsentType.WhatsApp, true, ConsentSource.Phone, null, Now.AddMinutes(1)).Should().BeFalse("то же состояние не записывается повторно");
        guest.RecordConsent(ConsentType.WhatsApp, false, ConsentSource.Phone, null, Now.AddMinutes(2)).Should().BeTrue();

        guest.Consents.Should().HaveCount(2);
        guest.CurrentConsent(ConsentType.WhatsApp)!.Granted.Should().BeFalse();
        guest.CurrentConsent(ConsentType.PersonalData).Should().BeNull();
    }

    [Fact]
    public void FillMissing_DoesNotOverwriteExistingContacts()
    {
        var guest = NewGuest();

        guest.FillMissing(PhoneNumber.Parse("+996700999999").Value, "other@example.com", Now);

        guest.Phone.Should().Be("+996555123456");
        guest.Email.Should().Be("aigul@example.com");
    }

    [Fact]
    public void Anonymize_ErasesPersonalData_KeepsConsentHistory_AndRefusesLinkedAccount()
    {
        var guest = NewGuest();
        guest.Update("Айгуль", PhoneNumber.Parse("+996555123456").Value, null, "aigul@example.com", new DateOnly(1990, 1, 1), "заметка", Now);
        guest.RecordConsent(ConsentType.PersonalData, true, ConsentSource.FrontDesk, null, Now);

        guest.Anonymize(Now).IsSuccess.Should().BeTrue();

        guest.Should().BeEquivalentTo(new { FullName = Guest.AnonymizedName, Phone = (string?)null, Email = (string?)null, BirthDate = (DateOnly?)null, Notes = (string?)null, IsAnonymized = true });
        guest.Consents.Should().ContainSingle();
        guest.Anonymize(Now).Error!.Code.Should().Be("guest.anonymized");

        var portal = NewGuest();
        portal.LinkUser(Guid.NewGuid(), Now);
        portal.Anonymize(Now).Error!.Code.Should().Be("guest.has_account");
    }
}
