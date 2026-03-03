namespace ContactCenterPOC.Services
{
    internal static class PhoneNumberMasker
    {
        public static string Mask(string? phoneNumber, int keepLastDigits = 4)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                return string.Empty;
            }

            if (keepLastDigits <= 0)
            {
                keepLastDigits = 0;
            }

            var chars = phoneNumber.ToCharArray();
            var digitsSeenFromEnd = 0;

            for (var i = chars.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(chars[i]))
                {
                    continue;
                }

                digitsSeenFromEnd++;
                if (digitsSeenFromEnd > keepLastDigits)
                {
                    chars[i] = '*';
                }
            }

            // If we didn't see enough digits, return as-is.
            if (digitsSeenFromEnd <= keepLastDigits)
            {
                return phoneNumber;
            }

            return new string(chars);
        }
    }
}
