namespace TokenAPI.Common.Extensions
{
    public class CustomUnauthorizedException : Exception
    {
        public object ErrorDetails { get; }
        public CustomUnauthorizedException(object errorDetails)
        {
            ErrorDetails = errorDetails;
        }
    }
}
