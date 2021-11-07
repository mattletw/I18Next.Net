using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using I18Next.Net.Backends;
using I18Next.Net.TranslationTrees;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace I18Next.Net.RemoteJsonFileBackend
{
    public class RemoteFileBackend : ITranslationBackend
    {
        private readonly IOptionsSnapshot<RemoteJsonFileOptions> _optionsSnapshot;
        private readonly IHttpClientFactory _httpClientFactory;
        
        public RemoteFileBackend(IHttpClientFactory httpClientFactory, IOptionsSnapshot<RemoteJsonFileOptions> optionsSnapshot)
        {
            _httpClientFactory = httpClientFactory;
            _optionsSnapshot = optionsSnapshot;
            _treeBuilderFactory = new GenericTranslationTreeBuilderFactory<HierarchicalTranslationTreeBuilder>();
        }
        
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        public async Task<ITranslationTree> LoadNamespaceAsync(string language, string @namespace)
        {
            JObject parsedJson;
            
            var client = _httpClientFactory.CreateClient(Constants.HttpClientName);

            var url = _optionsSnapshot.Value.Url;

            url = url.Replace("{{lng}}", language);
            url = url.Replace("{{ns}}", @namespace);
            
            using (Stream s = client.GetStreamAsync(url).Result)
            using (StreamReader sr = new StreamReader(s, Encoding))
            using (JsonReader reader = new JsonTextReader(sr))
            {
                parsedJson = (JObject) await JToken.ReadFromAsync(reader);
            }

            var builder = _treeBuilderFactory.Create();

            PopulateTreeBuilder("", parsedJson, builder);
            return builder.Build();
        }
        
        private readonly ITranslationTreeBuilderFactory _treeBuilderFactory;

        private static void PopulateTreeBuilder(string path, JObject node, ITranslationTreeBuilder builder)
        {
            if (path != string.Empty)
                path = path + ".";

            foreach (var childNode in node)
            {
                var key = path + childNode.Key;

                if (childNode.Value is JObject jObj)
                    PopulateTreeBuilder(key, jObj, builder);
                else if (childNode.Value is JValue jVal)
                    builder.AddTranslation(key, jVal.Value.ToString());
            }
        }
    }
}
