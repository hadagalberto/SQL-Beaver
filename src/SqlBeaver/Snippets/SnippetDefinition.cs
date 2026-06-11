using System.Runtime.Serialization;

namespace SqlBeaver.Snippets
{
    [DataContract(Namespace = "")]
    public sealed class SnippetDefinition
    {
        [DataMember(Name = "shortcut")] public string Shortcut { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "expansion")] public string Expansion { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
    }

    [DataContract(Namespace = "")]
    public sealed class SnippetFile
    {
        [DataMember(Name = "snippets")] public SnippetDefinition[] Snippets { get; set; }
    }
}
