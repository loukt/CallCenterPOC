using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class CriterionScore
    {
        [JsonProperty("criterionName")]
        public string CriterionName { get; set; } = string.Empty;

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("justification")]
        public string Justification { get; set; } = string.Empty;
    }
}
