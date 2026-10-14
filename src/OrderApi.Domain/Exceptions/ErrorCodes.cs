namespace OrderApi.Domain.Exceptions;

public static class ErrorCodes
{
    public const string ValidationRequest = "VALIDATION-01";
    public const string ValidationOrderNotEditable = "VALIDATION-02";
    public const string ValidationOrderEmpty = "VALIDATION-03";
    public const string ValidationInvalidTransition = "VALIDATION-04";
    public const string ValidationInvalidFilter = "VALIDATION-05";
    public const string ValidationUnknownSort = "VALIDATION-06";
    public const string ValidationCustomerNotFound = "VALIDATION-07";
    public const string ValidationAlreadyCanceled = "VALIDATION-08";

    public const string CustomerNotFound = "RESOURCE-NOT-FOUND-01";
    public const string OrderNotFound = "RESOURCE-NOT-FOUND-02";
    public const string ItemNotFound = "RESOURCE-NOT-FOUND-03";

    public const string ConflictConcurrentModification = "CONFLICT-00";
    public const string ConflictTaxId = "CONFLICT-01";
    public const string ConflictPassportNumber = "CONFLICT-02";
    public const string ConflictCustomerHasOrders = "CONFLICT-03";

    public const string AuthUnauthenticated = "AUTH-01";
    public const string AuthForbidden = "AUTH-02";

    public const string RateLimit = "RATE-00";
    public const string Internal = "INTERNAL-00";
}
