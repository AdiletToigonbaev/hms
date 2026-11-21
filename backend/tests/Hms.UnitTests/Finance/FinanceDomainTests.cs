using Hms.Modules.Finance.Contracts;
using Hms.Modules.Finance.Domain;

namespace Hms.UnitTests.Finance;

/// <summary>Платежи, сторно, возвраты и расходы (Sprint 6).</summary>
public class FinanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    private static Payment Paid(decimal amount = 1000m) =>
        Payment.RegisterManual(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PaymentType.Stay, PaymentMethod.Cash, amount, Guid.NewGuid(), null, Now).Value;

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.555)]
    public void RegisterManual_RejectsBadAmounts(decimal amount)
    {
        Payment.RegisterManual(Guid.NewGuid(), null, null, PaymentType.Other, PaymentMethod.Cash, amount, null, null, Now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void RegisterManual_IsPaidImmediately_AndLogged()
    {
        var p = Paid();

        p.Status.Should().Be(PaymentStatus.Paid);
        p.Channel.Should().Be(PaymentChannel.Manual);
        p.NetAmount.Should().Be(1000m);
        p.Transactions.Should().ContainSingle().Which.Kind.Should().Be(PaymentTransactionKind.Charge);
    }

    [Fact]
    public void Reverse_CreatesNegativeMirror_AndNetsToZero()
    {
        var p = Paid();

        var reversal = p.Reverse("Ошиблись суммой", hasActiveRefunds: false, null, Now).Value;

        p.Status.Should().Be(PaymentStatus.Reversed);
        reversal.Type.Should().Be(PaymentType.Reversal);
        reversal.Amount.Should().Be(-1000m);
        reversal.ReversalOfPaymentId.Should().Be(p.Id);
        (p.NetAmount + reversal.NetAmount).Should().Be(0m);
    }

    [Fact]
    public void Reverse_Twice_OrWithRefunds_OrWithoutReason_IsRejected()
    {
        var p = Paid();
        p.Reverse("", false, null, Now).Error!.Code.Should().Be("payment.reason_required");
        p.Reverse("Причина", hasActiveRefunds: true, null, Now).Error!.Code.Should().Be("payment.reversal_not_allowed");
        p.Reverse("Причина", false, null, Now).IsSuccess.Should().BeTrue();
        p.Reverse("Причина", false, null, Now).Error!.Code.Should().Be("payment.already_reversed");
    }

    [Fact]
    public void Reversal_ItselfCannotBeReversedOrRefunded()
    {
        var reversal = Paid().Reverse("Ошибка", false, null, Now).Value;

        reversal.Reverse("Ещё раз", false, null, Now).IsFailure.Should().BeTrue();
        reversal.IsRefundable.Should().BeFalse();
    }

    [Fact]
    public void Refund_CannotExceedPayment_IncludingEarlierRefunds()
    {
        var p = Paid(1000m);

        Refund.Request(p, 1001m, "Причина", PaymentMethod.Cash, 0m, null, Now).Error!.Code.Should().Be("refund.amount_exceeds_payment");
        Refund.Request(p, 400m, "Причина", PaymentMethod.Cash, 700m, null, Now).Error!.Code.Should().Be("refund.amount_exceeds_payment");
        Refund.Request(p, 300m, "Причина", PaymentMethod.Cash, 700m, null, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Refund_Flow_RequestApprovePay_UpdatesPaymentStatus()
    {
        var p = Paid(1000m);
        var r = Refund.Request(p, 400m, "Выехал раньше", PaymentMethod.Cash, 0m, null, Now).Value;

        r.MarkPaid(null, Now).IsFailure.Should().BeTrue("сначала решение");
        r.Approve(null, null, null, Now).IsSuccess.Should().BeTrue();
        p.ApplyRefunds(approvedNotPaid: 400m, paid: 0m, null, Now);
        p.Status.Should().Be(PaymentStatus.RefundPending);

        r.MarkPaid(null, Now).IsSuccess.Should().BeTrue();
        p.ApplyRefunds(approvedNotPaid: 0m, paid: 400m, null, Now);
        p.Status.Should().Be(PaymentStatus.PartiallyRefunded);
        p.RefundedAmount.Should().Be(400m);
        p.NetAmount.Should().Be(600m);
        p.Transactions.Should().Contain(t => t.Kind == PaymentTransactionKind.Refund && t.Amount == -400m);
    }

    [Fact]
    public void Refund_FullAmount_MarksPaymentRefunded()
    {
        var p = Paid(500m);
        p.ApplyRefunds(0m, 500m, null, Now);

        p.Status.Should().Be(PaymentStatus.Refunded);
        p.IsRefundable.Should().BeFalse();
        p.NetAmount.Should().Be(0m);
    }

    [Fact]
    public void Refund_Approve_RespectsApproverLimit()
    {
        var r = Refund.Request(Paid(5000m), 3000m, "Отмена", PaymentMethod.Cash, 0m, null, Now).Value;

        r.Approve(2000m, null, null, Now).Error!.Code.Should().Be("refund.limit_exceeded");
        r.Status.Should().Be(RefundStatus.Requested);
        r.Approve(3000m, null, null, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Refund_Reject_NeedsComment_AndIsFinal()
    {
        var r = Refund.Request(Paid(), 100m, "Причина", PaymentMethod.Cash, 0m, null, Now).Value;

        r.Reject("", null, Now).IsFailure.Should().BeTrue();
        r.Reject("Не положено по правилам", null, Now).IsSuccess.Should().BeTrue();
        r.IsActive.Should().BeFalse();
        r.Approve(null, null, null, Now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Expense_Void_BlocksFurtherChanges()
    {
        var e = Expense.Create(Guid.NewGuid(), Guid.NewGuid(), 2500m, new DateOnly(2026, 11, 20), PaymentMethod.Cash, "Прачечная", "Стирка белья", null, Now).Value;

        e.Void("Внесли дважды", null, Now).IsSuccess.Should().BeTrue();
        e.Update(Guid.NewGuid(), 100m, new DateOnly(2026, 11, 20), PaymentMethod.Cash, null, "Правка", Now).Error!.Code.Should().Be("expense.voided");
        e.Void("Ещё раз", null, Now).IsFailure.Should().BeTrue();
    }
}
