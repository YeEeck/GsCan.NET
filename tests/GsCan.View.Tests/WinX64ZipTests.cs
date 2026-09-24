namespace GsCan.View.Tests
{
    public class WinX64ZipTests
    {
        [Fact]
        public void publish_script_exists()
        {
            Assert.True(File.Exists(Path.Combine(RepoRoot, "scripts", "publish-view.ps1")));
        }

        [Fact]
        public void view_csproj_imports_gscan_targets()
        {
            var csproj = File.ReadAllText(ViewCsprojPath);
            Assert.Contains(@"<Import Project=""..\GsCan\build\GsCan.targets"" />", csproj);
        }

        [Fact]
        public void view_csproj_is_not_publish_single_file()
        {
            var csproj = File.ReadAllText(ViewCsprojPath);
            Assert.Contains("<PublishSingleFile>false</PublishSingleFile>", csproj);
            Assert.DoesNotContain("<PublishSingleFile>true</PublishSingleFile>", csproj);
        }

        private static string ViewCsprojPath => Path.Combine(RepoRoot, "src", "GsCan.View", "GsCan.View.csproj");

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "GsCan.sln")))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException(
                    "GsCan.sln not found walking up from " + AppContext.BaseDirectory);
            }
        }
    }
}
