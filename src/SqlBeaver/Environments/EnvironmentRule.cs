using System.Runtime.Serialization;

namespace SqlBeaver.Environments
{
    [DataContract(Namespace = "")]
    public sealed class EnvironmentRule
    {
        [DataMember(Name = "name")]           public string   Name           { get; set; }
        [DataMember(Name = "color")]          public string   Color          { get; set; }
        [DataMember(Name = "servers")]        public string[] Servers        { get; set; }
        [DataMember(Name = "databases")]      public string[] Databases      { get; set; }
        [DataMember(Name = "confirmExecute")] public bool     ConfirmExecute { get; set; }
    }

    [DataContract(Namespace = "")]
    public sealed class EnvironmentFile
    {
        [DataMember(Name = "environments")] public EnvironmentRule[] Environments { get; set; }
    }
}
