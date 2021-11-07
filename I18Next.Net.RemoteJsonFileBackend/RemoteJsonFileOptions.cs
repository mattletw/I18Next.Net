using System.ComponentModel.DataAnnotations;

namespace I18Next.Net.RemoteJsonFileBackend
{
    public class RemoteJsonFileOptions
    {
        [Required]
        public int CacheTTL { get; set; }

        [Required]
        public string Url { get; set; }
    }
}
