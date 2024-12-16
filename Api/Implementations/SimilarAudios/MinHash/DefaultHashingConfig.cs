namespace Api.Implementations.SimilarAudios.MinHash
{
    /// <summary>
    ///  Default hashing configuration for LSH schema.
    /// </summary>
    public class DefaultHashingConfig : HashingConfig
    {
        /// <summary>
        ///  Initializes a new instance of the <see cref="DefaultHashingConfig"/> class.
        /// </summary>
        public DefaultHashingConfig()
        {
            NumberOfLshTables = 25;
            NumberOfMinHashesPerTable = 4;
            HashBuckets = 0;
            Width = 128;
            Height = 32;
        }
    }
}