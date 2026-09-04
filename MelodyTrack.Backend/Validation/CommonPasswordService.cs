namespace MelodyTrack.Backend.Validation;

public interface ICommonPasswordService
{
    bool Contains(string password);
}

public sealed class CommonPasswordService : ICommonPasswordService
{
    private const string CommonPasswordsFileName = "common_passwords.txt";
    private readonly string? content;

    public CommonPasswordService(IWebHostEnvironment environment)
    {
        var path = ResolvePath(environment.ContentRootPath);
        content = path is null ? null : File.ReadAllText(path);
    }

    public bool Contains(string password) =>
        content?.Contains(password, StringComparison.Ordinal) == true;

    private static string? ResolvePath(string contentRootPath)
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
