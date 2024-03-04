using Database.Interfaces;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
using System.Collections.ObjectModel;
using static NRedisStack.Search.Schema;

namespace Database.Implementations
{
    public class DbHelpers : IDbHelpers
    {
        private readonly IConnectionMultiplexer _connection;

        public DbHelpers(IConnectionMultiplexer connection)
        {
            _connection = connection;
        }

        public IReadOnlyCollection<int> GetSimilarHashIndex(string hash, int position, int limit)
        {
            var database = _connection.GetDatabase();

            var ft = database.FT();

            if (position == 0)
            {
                var server = _connection.GetServer("localhost:6379");
                server.FlushDatabase();

                var schema = new Schema()
                    .AddNumericField("position")
                    .AddTextField("path")
                    .AddVectorField("hash",
                             VectorField.VectorAlgo.HNSW,
                             new Dictionary<string, object>()
                             {
                                 ["TYPE"] = "FLOAT32",
                                 ["DIM"] = "121",
                                 ["DISTANCE_METRIC"] = "L2"
                             });

                try { ft.DropIndex("idx:hashes"); } catch { }

                try { ft.Create("idx:hashes", new FTCreateParams().On(IndexDataType.HASH), schema); } catch (RedisServerException) { }

                ft.ConfigSet("MAXSEARCHRESULTS", "-1");
            }

            var hashArray = Convert.FromHexString(hash).SelectMany(val => BitConverter.GetBytes(val / 255f)).ToArray();

            // @position:[$position +inf] 
            var query = new Query($"@hash:[VECTOR_RANGE $range $hash]")
                .AddParam("range", 0.9)
                .AddParam("hash", hashArray)
                .Limit(0, limit)
                .Dialect(2);

            return new ReadOnlyCollection<int>(ft.Search("idx:hashes", query).Documents
                .Select(result => int.Parse(result.Id)).ToList());
        }

        public void InsertPartialImageHash(string hash, int position, string path)
        {
            var database = _connection.GetDatabase();

            var ft = database.FT();

            // Convert pixel value from 0-255 to float between 0-1 to be able to use redis' vector similarity functions
            var hashArray = Convert.FromHexString(hash).SelectMany(val => BitConverter.GetBytes(val / 255f)).ToArray();

            database.HashSet(position.ToString(), [new HashEntry("position", position), new HashEntry("path", path), new HashEntry("hash", hashArray)]);
        }
    }
}
