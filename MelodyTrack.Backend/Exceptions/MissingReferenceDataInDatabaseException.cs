namespace MelodyTrack.Backend.Exceptions;

public class MissingReferenceDataInDatabaseException(string type, string value)
    : Exception($"Database does not contain required {type} '{value}'");
