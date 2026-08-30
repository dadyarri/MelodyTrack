using MelodyTrack.Backend.GodMode;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class GodModeHtmlTests
{
    [Fact]
    public void Page_SpecificNoticeAudience_ProvidesRecipientPickerWithoutRawIdPrompts()
    {
        var page = GodModeHtml.Page;

        page.ShouldContain("Найти по имени, почте или роли");
        page.ShouldContain("data-recipient-type");
        page.ShouldContain("Выберите хотя бы одного получателя.");
        page.ShouldContain("grid-template-columns:1fr");
        page.ShouldNotContain("ID пользователей через запятую");
        page.ShouldNotContain("ID клиентов через запятую");
    }

    [Fact]
    public void Page_NoticeEditing_ReusesVisibleFormWithoutPromptSequence()
    {
        var page = GodModeHtml.Page;

        page.ShouldContain("noticeForm.submit.textContent='Сохранить'");
        page.ShouldContain("noticeForm.cancel.hidden=false");
        page.ShouldNotContain("prompt('Заголовок:'");
        page.ShouldNotContain("prompt('Аудитория:");
    }

    [Fact]
    public void Page_NoticeBody_IsMarkedOptionalAndNotRequiredByClientValidation()
    {
        var page = GodModeHtml.Page;

        page.ShouldContain("Текст <span class=\"muted\">(необязательно)</span>");
        page.ShouldContain("if(!noticeForm.title.value.trim())");
        page.ShouldNotContain("Заполните заголовок и текст.");
    }
}
