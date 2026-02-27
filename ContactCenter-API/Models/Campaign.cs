using System.ComponentModel.DataAnnotations;

namespace ContactCenterPOC.Models
{
    public class Campaign
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string AiBehaviorInstructions { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class CreateCampaignRequest
    {
        [Required(ErrorMessage = "Title is required")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 200 characters")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        [StringLength(1000, MinimumLength = 1, ErrorMessage = "Description must be between 1 and 1000 characters")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "AI behavior instructions are required")]
        public string AiBehaviorInstructions { get; set; } = string.Empty;
    }
}
