namespace TokenAPI.Common
{
    public static class ApiConstants
    {
        public const int STATUS_OK = 200;
        public const int STATUS_CREATED = 201;
        public const int STATUS_NO_CONTENT = 204;
        public const int STATUS_BAD_REQUEST = 400;
        public const int STATUS_UNAUTHORIZED = 401;
        public const int STATUS_FORBIDDEN = 403;
        public const int STATUS_NOT_FOUND = 404;
        public const int STATUS_CONFLICT = 409;
        public const int STATUS_SERVER_ERROR = 500;
        public const int STATUS_SERVICE_SERVICE_UNAVAILABLE = 503;

        public const string INVALID_MODEL = "Invalid model state.";
        public const string SERVER_ERROR = "Internal server error.";
        public const string UNAUTHORIZED = "Unauthorized.";
        public const string FORBIDDEN = "Forbidden.";
        public const string NOT_FOUND = "Resource not found.";
        public const string CONFLICT = "Conflict.";
        public const string SERVICE_UNAVAILABLE = "Service unavailable.";
    }
}
