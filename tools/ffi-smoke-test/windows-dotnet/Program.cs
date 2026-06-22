using System.Text.Json;
using Rynat.Client;

var bridge = new RynatCoreBridge();

var checks = new (string Name, Action Run)[]
{
    ("generate_link", () =>
    {
        var result = bridge.GenerateLink(new GenerateLinkRequest(
            "files.example",
            "Team",
            "/Project/file.pdf",
            "file"
        ));
        Console.WriteLine(result.HttpUrl);
    }),
    ("activate_link", () =>
    {
        var result = bridge.ActivateLink(new ActivateLinkRequest(
            "rynat://s?h=files.example&s=Team&p=/Project/file.pdf&t=file"
        ));
        Console.WriteLine(result.BrowseLocation.RemotePath);
    }),
    ("preview_plan", () =>
    {
        var result = bridge.PreviewPlan(new PreviewPlanRequest(
            "files.example",
            "Team",
            "/Project/image.jpg",
            "file",
            512
        ));
        Console.WriteLine($"{result.ContentType}:{result.CacheKey}");
    }),
    ("upload_plan", () =>
    {
        var result = bridge.UploadPlan(new UploadPlanRequest(
            @"C:\Temp\file.pdf",
            "files.example",
            "Team",
            "/Project/file.pdf"
        ));
        Console.WriteLine(result.Direction);
    }),
    ("smb_list_without_login_fails_cleanly", () =>
    {
        try
        {
            _ = bridge.SmbListDirectory(new SmbListDirectoryRequest(
                "Media",
                "/"
            ));
            throw new InvalidOperationException("Expected SMB list to fail without login.");
        }
        catch (RynatCoreBridgeException ex)
        {
            Console.WriteLine($"{ex.ErrorCode ?? "no_code"}:{ex.Message}");
        }
    }),
    ("task_request_json_serialization", () =>
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            connection_id = "test-connection",
            share = "Media",
            path = "/demo.mp4",
            local_path = @"C:\Temp\demo.mp4"
        });
        var request = new SmbStartTaskRequest(
            "cache_file",
            payload,
            "task-op-1",
            "profile-1",
            true
        );
        var json = JsonSerializer.Serialize(request, RynatJsonContext.Default.SmbStartTaskRequest);
        Console.WriteLine(json);
    }),
};

foreach (var (name, run) in checks)
{
    Console.WriteLine($"== {name} ==");
    run();
}

Console.WriteLine("Windows bridge smoke test passed.");
