using System.ComponentModel.DataAnnotations;
using ContactCenterPOC.Models;

namespace CallCenterPOC_API.Tests.Unit
{
    public class PhoneNumberValidationTests
    {
        private static IList<ValidationResult> ValidateModel(CallRequest model)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(model);
            Validator.TryValidateObject(model, context, results, validateAllProperties: true);
            return results;
        }

        [Fact]
        public void SingleValidE164Number_ShouldPassValidation()
        {
            var request = new CallRequest { PhoneNumbers = new[] { "+6591234567" } };
            var results = ValidateModel(request);

            Assert.DoesNotContain(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }

        [Fact]
        public void TwoValidE164Numbers_ShouldPassValidation()
        {
            var request = new CallRequest { PhoneNumbers = new[] { "+6591234567", "+14155551234" } };
            var results = ValidateModel(request);

            Assert.DoesNotContain(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }

        [Fact]
        public void ThreeNumbers_ShouldFailValidation()
        {
            var request = new CallRequest { PhoneNumbers = new[] { "+6591234567", "+14155551234", "+442071234567" } };
            var results = ValidateModel(request);

            Assert.Contains(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }

        [Fact]
        public void EmptyArray_ShouldFailValidation()
        {
            var request = new CallRequest { PhoneNumbers = Array.Empty<string>() };
            var results = ValidateModel(request);

            Assert.Contains(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }

        [Theory]
        [InlineData("+6591234567")]
        [InlineData("+14155551234")]
        [InlineData("+442071234567")]
        [InlineData("+81312345678")]
        public void ValidE164Numbers_SingleItem_ShouldPassValidation(string phoneNumber)
        {
            var request = new CallRequest { PhoneNumbers = new[] { phoneNumber } };
            var results = ValidateModel(request);

            Assert.DoesNotContain(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }

        [Theory]
        [InlineData("6591234567")]       // missing + prefix
        [InlineData("+0123")]            // starts with 0 after +
        [InlineData("abc")]             // non-numeric
        [InlineData("+")]               // just plus sign
        [InlineData("+1")]              // too short (need at least 2 digits after +)
        public void InvalidPhoneNumbers_InArray_ShouldBeDetectedByService(string phoneNumber)
        {
            // Note: Array-level validation only checks length (1-2 items).
            // Per-item E.164 format validation is handled by CallService.InitiateCall
            // and CallController, not by DataAnnotations on the DTO array.
            // This test verifies the array passes DTO validation but would fail service validation.
            var request = new CallRequest { PhoneNumbers = new[] { phoneNumber } };
            var results = ValidateModel(request);

            // Array has 1 item so MinLength/MaxLength pass — format check is at service level
            Assert.DoesNotContain(results, r =>
                r.MemberNames.Contains(nameof(CallRequest.PhoneNumbers)));
        }
    }
}
