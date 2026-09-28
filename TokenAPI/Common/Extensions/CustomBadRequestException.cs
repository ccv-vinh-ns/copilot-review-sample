namespace TokenAPI.Common.Extensions
{
    public class CustomBadRequestException : Exception
    {
        public object ErrorDetails { get; }
        public CustomBadRequestException(object errorDetails)
        {
            ErrorDetails = errorDetails;
        }
    }
}
