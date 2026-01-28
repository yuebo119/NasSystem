#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

using Microsoft.Data.Sqlite;

var connectionString = "Data Source=src/NasSystem.Api/data/nas.db";
using var connection = new SqliteConnection(connectionString);
connection.Open();

var command = connection.CreateCommand();
command.CommandText = "SELECT Id, Name, Path, ParentId FROM FileItems WHERE Path LIKE '/123%' OR Path = '/123';";

using var reader = command.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"Id: {reader.GetString(0)}, Name: {reader.GetString(1)}, Path: {reader.GetString(2)}, ParentId: {(reader.IsDBNull(3) ? "NULL" : reader.GetString(3))}");
}
