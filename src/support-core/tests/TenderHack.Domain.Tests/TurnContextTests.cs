using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class TurnContextTests
{
    [Fact]
    public void MergingANewSlotTypeAddsIt()
    {
        var sut = TurnContext.Empty.WithObservedQuestion("вопрос", [new ContextSlot("role", "поставщик", ContextSlotProvenance.UserExplicit)]);

        var slot = Assert.Single(sut.KnownSlots);
        Assert.Equal("role", slot.Type);
        Assert.Equal("поставщик", slot.Value);
        Assert.Equal(ContextSlotProvenance.UserExplicit, slot.Provenance);
    }

    [Fact]
    public void InferredNeverOverwritesAnAlreadyKnownExplicitSlot()
    {
        var sut = TurnContext.Empty
            .WithObservedQuestion("вопрос 1", [new ContextSlot("role", "поставщик", ContextSlotProvenance.UserExplicit)])
            .WithObservedQuestion("вопрос 2", [new ContextSlot("role", "заказчик", ContextSlotProvenance.Inferred)]);

        var slot = Assert.Single(sut.KnownSlots);
        Assert.Equal("поставщик", slot.Value);
        Assert.Equal(ContextSlotProvenance.UserExplicit, slot.Provenance);
    }

    [Fact]
    public void UserExplicitCorrectionOverwritesAnEarlierTrustedFact()
    {
        // product-experience.md §3 rule 2: the user can correct a displayed value; after correction
        // provenance becomes user_explicit, not "corrected trusted fact".
        var sut = TurnContext.Empty
            .WithObservedQuestion("вопрос 1", [new ContextSlot("role", "поставщик", ContextSlotProvenance.TrustedPortalContext)])
            .WithObservedQuestion("вопрос 2", [new ContextSlot("role", "заказчик", ContextSlotProvenance.UserExplicit)]);

        var slot = Assert.Single(sut.KnownSlots);
        Assert.Equal("заказчик", slot.Value);
        Assert.Equal(ContextSlotProvenance.UserExplicit, slot.Provenance);
    }

    [Fact]
    public void ClarificationIncrementsTheCounterAndRecordsMissingConditions()
    {
        var sut = TurnContext.Empty.WithClarification(["сумма контракта"]);

        Assert.Equal(1, sut.ConsecutiveClarifications);
        Assert.True(sut.HasPendingClarification);
        Assert.Equal(["сумма контракта"], sut.LastMissingConditions);
    }

    [Fact]
    public void DecliningTheCurrentClarificationRecordsItAsDeclined()
    {
        var sut = TurnContext.Empty.WithClarification(["сумма контракта"]).WithDeclinedCurrentClarification();

        Assert.Contains("сумма контракта", sut.DeclinedMissingConditions);
    }

    [Fact]
    public void DecliningTwiceDoesNotDuplicateTheDeclinedCondition()
    {
        var sut = TurnContext.Empty
            .WithClarification(["сумма контракта"])
            .WithDeclinedCurrentClarification()
            .WithClarification(["сумма контракта"])
            .WithDeclinedCurrentClarification();

        Assert.Single(sut.DeclinedMissingConditions);
    }

    [Fact]
    public void ResettingTheLoopClearsTheCounterButKeepsKnownSlots()
    {
        var sut = TurnContext.Empty
            .WithObservedQuestion("вопрос", [new ContextSlot("role", "поставщик", ContextSlotProvenance.UserExplicit)])
            .WithClarification(["сумма контракта"])
            .WithClarificationLoopReset();

        Assert.Equal(0, sut.ConsecutiveClarifications);
        Assert.False(sut.HasPendingClarification);
        Assert.Empty(sut.LastMissingConditions);
        Assert.Single(sut.KnownSlots); // known context survives scenario resolution
    }
}
