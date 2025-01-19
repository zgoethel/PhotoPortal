namespace PhotoPortal.Shared;

public partial class Submission
{
    public DateTime Submitted { get; set; }

    public string From { get; set; }

    public string Message { get; set; }

    public int NumAttachments { get; set; }

    public bool EmailMe { get; set; }

    public string EmailAddress { get; set; }

    public partial class FileBase64
    {
        public string OriginalName { get; set; }
        public string Base64 { get; set; }
    }

    public partial class WithFiles : Submission
    {
        public List<FileBase64> FileContents { get; set; } = [];
    }

    public partial class AdminView : Submission
    {
        public string FileBase { get; set; }

        public List<string> ImagePaths { get; set; } = [];

        public DateTime? Read { get; set; }
    }
}
