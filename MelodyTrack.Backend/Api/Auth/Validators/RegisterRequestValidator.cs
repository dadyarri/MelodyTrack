using System.IO.MemoryMappedFiles;
using System.Text;
using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class RegisterRequestValidator : Validator<RegisterRequest>
{
    public RegisterRequestValidator(IWebHostEnvironment env)
    {
        var contentRoot = env.ContentRootPath;

        RuleFor(e => e.Email)
            .NotEmpty()
            .WithMessage("Email обязателен")
            .EmailAddress()
            .WithMessage("Невалидный email");

        RuleFor(e => e.Password)
            .NotEmpty()
            .WithMessage("Пароль не должен быть пустым")
            .MinimumLength(8)
            .WithMessage("Минимальная длина пароля — 8 символов")
            .Matches("^(?=.*?[A-Z])(?=.*?[a-z])(?=.*?[0-9])(?=.*?[#?!@$%^&*-]).{8,}$")
            .WithMessage(
                "Пароль слишком простой: включите хотя бы одну заглавную латинскую букву, одну строчную, одну цифру и один спецсимвол")
            .Custom((value, ctx) =>
            {
                var path = Directory
                    .EnumerateFiles(contentRoot, "common_passwords.txt", SearchOption.TopDirectoryOnly)
                    .First();

                var fileSize = new FileInfo(path).Length;

                using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
                using var accessor = mmf.CreateViewAccessor(0, fileSize);
                var buffer = new byte[fileSize];
                accessor.ReadArray(0, buffer, 0, (int)fileSize);
                var fileContent = Encoding.UTF8.GetString(buffer);

                if (fileContent.Contains(value))
                {
                    ctx.AddFailure("Password", "Пароль не должен быть частоиспользуемым");
                }
            });
    }
}