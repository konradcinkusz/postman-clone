using System.Text.Json;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J7 (WP26): a cURL command is imported into a new collection; a Postman v2.1 file is imported; the collection is exported as
/// Postman v2.1 and the export is imported again. Each file is written to a temporary folder and deleted at the end of the test.
/// </summary>
public sealed class ImportExportJourneys : Journey
{
    [Fact]
    public async Task Curl_command_imports_then_postman_file_imports_then_export_reimports()
    {
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "postmanclone-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            // A cURL command is imported into a collection that already exists.
            var target = $"Curl target {Guid.NewGuid():N}";
            await Ui.OpenAsync(page, "/");
            await Workspace.CreateCollectionAsync(page, target);

            await Ui.OpenAsync(page, "/import");
            await page.GetByTestId("import-tab-curl").ClickAsync();
            await page.GetByTestId("import-curl-collection").SelectOptionAsync(new SelectOptionValue { Label = target });
            await page.GetByTestId("import-curl-command").FillAsync("curl -X GET 'http://example.test/api/curl-import' -H 'Accept: application/json'");
            await page.GetByTestId("import-curl-submit").ClickAsync();
            await Expect(page.GetByTestId("import-result")).ToContainTextAsync("Import finished");

            var collectionName = $"Postman {Guid.NewGuid():N}";
            var file = Path.Combine(tempDirectory, "collection.json");
            var postmanFile = JsonSerializer.Serialize(new
            {
                info = new { name = collectionName, schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
                item = new[] { new { name = "Ping", request = new { method = "GET", url = "http://example.test/ping" } } },
            });
            await File.WriteAllTextAsync(file, postmanFile, TestContext.Current.CancellationToken);

            await Ui.OpenAsync(page, "/import");
            await page.GetByTestId("import-file").SetInputFilesAsync(file);
            await page.GetByTestId("import-file-submit").ClickAsync();
            await Expect(page.GetByTestId("import-result")).ToContainTextAsync("Import finished");

            await Ui.OpenAsync(page, "/");
            await Workspace.ExpandAsync(page, collectionName);
            await Expect(page.GetByTestId("tree")).ToContainTextAsync(collectionName);
            await Workspace.SelectAsync(page, collectionName);

            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByTestId("action-export-postman").ClickAsync());
            var exported = Path.Combine(tempDirectory, "export.json");
            await download.SaveAsAsync(exported);
            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(exported, TestContext.Current.CancellationToken)))
            {
                var root = document.RootElement;
                Assert.Equal(collectionName, root.GetProperty("info").GetProperty("name").GetString());
                Assert.Equal("Ping", root.GetProperty("item")[0].GetProperty("name").GetString());
            }

            await Ui.OpenAsync(page, "/import");
            await page.GetByTestId("import-file").SetInputFilesAsync(exported);
            await page.GetByTestId("import-file-submit").ClickAsync();
            await Expect(page.GetByTestId("import-result")).ToContainTextAsync("Import finished");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
