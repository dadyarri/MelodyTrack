using System.IO.MemoryMappedFiles;
using System.Text;
using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class RegisterRequestValidator : Validator<RegisterRequest>
{
    private const string CommonPasswordsFileName = "common_passwords.txt";

    public RegisterRequestValidator(IWebHostEnvironment env)
    {
        var commonPasswordsPath = ResolveCommonPasswordsPath(env.ContentRootPath);

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
                if (commonPasswordsPath is null)
                {
                    return;
                }

                var fileSize = new FileInfo(commonPasswordsPath).Length;

                using var mmf = MemoryMappedFile.CreateFromFile(
                    commonPasswordsPath,
                    FileMode.Open,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read);
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

    private static string? ResolveCommonPasswordsPath(string contentRootPath)
    {
        foreach (var basePath in new[] { contentRootPath, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(basePath);
            while (current is not null)
            {
                var directCandidate = Path.Combine(current.FullName, CommonPasswordsFileName);
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }

                var projectCandidate = Path.Combine(current.FullName, "MelodyTrack.Backend", CommonPasswordsFileName);
                if (File.Exists(projectCandidate))
                {
                    return projectCandidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}
