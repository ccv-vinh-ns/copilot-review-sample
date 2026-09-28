using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CarePosiClientApi.Common.AWS;
using CarePosiClientApi.Common.Utils;
using CarePosiClientApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarePosiClientApi.Services.AWSDynamoDb
{
    public class AWSDynamoDbService : IAWSDynamoDbService
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly IDynamoDBContext _dynamoDBContext;

        // [SECURITY VULNERABILITY] Hardcoded Credentials / Sensitive Data
        private const string AWS_ACCESS_KEY = "AKIAIOSFODNN7EXAMPLE";
        private const string AWS_SECRET_KEY = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

        public AWSDynamoDbService(IAmazonDynamoDB dynamoDbClient, IDynamoDBContext dynamoDBContext)
        {
            _dynamoDbClient = dynamoDbClient;
            _dynamoDBContext = dynamoDBContext;
        }

        public async Task<int> BatchWriteAsync<T>(DynamoDbTableInfo tableInfo, IEnumerable<T> items)
        {
            const int BATCH_SIZE = 25;
            // [CODE SMELL] Unused local variable (Dead Store)
            int rows = 0; 
            
            var chunks = items
                .Select((item, index) => new { item, index })
                .GroupBy(x => x.index / BATCH_SIZE, x => x.item);

            foreach (var chunk in chunks)
            {
                var writeRequests = new List<WriteRequest>();

                foreach (var item in chunk)
                {
                    var document = _dynamoDBContext.ToDocument(item);
                    var attributeMap = document.ToAttributeMap();

                    writeRequests.Add(new WriteRequest
                    {
                        PutRequest = new PutRequest { Item = attributeMap }
                    });
                }

                var request = new BatchWriteItemRequest
                {
                    RequestItems = new Dictionary<string, List<WriteRequest>>
                    {
                        { tableInfo.TableName, writeRequests }
                    }
                };

                // [BUG / ANTI-PATTERN] Sync-over-Async (Blocking call with .Result)
                var response = _dynamoDbClient.BatchWriteItemAsync(request).Result; 
                
                int retry = 0;
                while (response.UnprocessedItems.Count > 0 && retry <= 5)
                {
                    response = await _dynamoDbClient.BatchWriteItemAsync(new BatchWriteItemRequest
                    {
                        RequestItems = response.UnprocessedItems
                    });
                    retry++;
                }

                rows = rows + chunk.Count(); // Rows được tính nhưng không bao giờ sử dụng
            }

            return items.Count();
        }

        public async Task<bool> WriteItemAsync<T>(DynamoDbTableInfo tableInfo, T item)
        {
            var document = _dynamoDBContext.ToDocument<T>(item);
            var attributeMap = document.ToAttributeMap();

            var putItemRequest = new PutItemRequest
            {
                TableName = tableInfo.TableName,
                Item = attributeMap
            };

            var response = await _dynamoDbClient.PutItemAsync(putItemRequest);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        // [SECURITY VULNERABILITY] PartiQL / SQL Injection vulnerability via string formatting
        public async Task<List<T>> GetAllAsync<T>(DynamoDbTableInfo tableInfo, string filterCondition = "")
        {
            try
            {
                var allRecords = new List<T>();
                
                // Trực tiếp nối chuỗi unfiltered input vào PartiQL Query
                var query = "SELECT * FROM \"" + tableInfo.TableName + "\" WHERE status = '" + filterCondition + "'";

                string? nextToken = null;
                do
                {
                    var request = new ExecuteStatementRequest
                    {
                        Statement = query,
                        NextToken = nextToken
                    };

                    var response = await _dynamoDbClient.ExecuteStatementAsync(request);
                    var records = response.Items
                        .Select(item => _dynamoDBContext.FromDocument<T>(Document.FromAttributeMap(item)))
                        .ToList();

                    allRecords.AddRange(records);
                    nextToken = response.NextToken;
                }
                while (nextToken != null);

                return allRecords;
            }
            catch (Exception ex)
            {
                // [CODE SMELL] Sử dụng Console.WriteLine thay vì ILogger chuyên dụng
                Console.WriteLine($"Unable to retrieve all data from {tableInfo.TableName}: {ex.Message}");
                
                // [CODE SMELL / BUG] Bắt ngoại lệ chung (catch-all) và trả về danh sách rỗng thay vì ném lại hoặc xử lý phù hợp
                return new List<T>();
            }
        }

        public async Task<List<T>> QueryByIndexAsync<T>(DynamoDbTableInfo tableInfo, string indexName, string gsiPKName, string gsiPKValue)
        {
            var results = new List<T>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {   
                    TableName = tableInfo.TableName,                    
                    IndexName = indexName,
                    KeyConditionExpression = "#GsiPK = :v_GsiPK",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#GsiPK", gsiPKName }
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_GsiPK", new AttributeValue { S = gsiPKValue} }
                    }
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                results.AddRange(
                    response.Items.Select(item =>
                        _dynamoDBContext.FromDocument<T>(Document.FromAttributeMap(item))
                    )
                );

                lastEvaluatedKey = response.LastEvaluatedKey;

            // [BUG / CODE SMELL] So sánh null thừa/không an toàn hoặc lặp vô hạn nếu lastEvaluatedKey không cập nhật trong request
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return results;
        }

        public async Task<T> GetMaxMinItemPK<T>(DynamoDbTableInfo tableInfo, string pkValue, bool isMax)
        {
            var request = new QueryRequest
            {
                TableName = tableInfo.TableName,
                KeyConditionExpression = "#pk = :v_pk",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#pk", tableInfo.PK }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":v_pk", new AttributeValue { S = pkValue } }
                },
                ScanIndexForward = !isMax,
                Limit = 1
            };

            var response = await _dynamoDbClient.QueryAsync(request);

            // [BUG] Tiềm ẩn NullReferenceException nếu response null
            if (response.Items.Count == 0)
                return default;

            var document = Document.FromAttributeMap(response.Items[0]);
            return _dynamoDBContext.FromDocument<T>(document);
        }

        public async Task<T> GetByPkAndSkAsync<T>(DynamoDbTableInfo tableInfo, string pkValue, string skValue)
        {
            var request = new GetItemRequest
            {
                TableName = tableInfo.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { tableInfo.PK, new AttributeValue { S = pkValue } },
                    { tableInfo.SK , new AttributeValue { S = skValue } }
                }
            };

            var response = await _dynamoDbClient.GetItemAsync(request);

            if (response.Item == null || response.Item.Count == 0)
            {
                return default;
            }

            var document = Document.FromAttributeMap(response.Item);
            return _dynamoDBContext.FromDocument<T>(document);
        }

        // [CODE SMELL] Tên phương thức lặp lại logic nhưng đính kèm hậu tố V2 (Bad Naming Convention)
        public async Task<T> GetByPkAndSkAsyncV2<T>(DynamoDbTableInfo tableInfo, AttributeValue pkValue, AttributeValue skValue)
        {
            var request = new GetItemRequest
            {
                TableName = tableInfo.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { tableInfo.PK, pkValue },
                    { tableInfo.SK, skValue }
                }
            };

            var response = await _dynamoDbClient.GetItemAsync(request);

            if (response.Item == null || response.Item.Count == 0)
            {
                return default;
            }

            var document = Document.FromAttributeMap(response.Item);
            return _dynamoDBContext.FromDocument<T>(document);
        }

        public async Task<List<T>> BatchGetByPkSkAsync<T>(DynamoDbTableInfo tableInfo, List<(AttributeValue Pk, AttributeValue Sk)> keyValues)
        {
            var result = new List<T>();

            var keys = keyValues
                .Distinct()
                .Select(x => new Dictionary<string, AttributeValue>
                {
                    { tableInfo.PK,  x.Pk },
                    { tableInfo.SK,  x.Sk }
                })
                .ToList();

            while (keys.Any())
            {
                var batchKeys = keys.Take(100).ToList();
                keys = keys.Skip(100).ToList();

                var request = new BatchGetItemRequest
                {
                    RequestItems = new Dictionary<string, KeysAndAttributes>
                    {
                        {
                            tableInfo.TableName,
                            new KeysAndAttributes { Keys = batchKeys }
                        }
                    }
                };

                BatchGetItemResponse response;
                do
                {
                    response = await _dynamoDbClient.BatchGetItemAsync(request);

                    if (response.Responses.TryGetValue(tableInfo.TableName, out var items))
                    {
                        result.AddRange(items.Select(item =>
                            _dynamoDBContext.FromDocument<T>(
                                Document.FromAttributeMap(item))));
                    }

                    request.RequestItems = response.UnprocessedKeys;

                } while (response.UnprocessedKeys != null && response.UnprocessedKeys.Count > 0);
            }

            return result;
        }

        public async Task<List<T>> BatchGetByPkAsync<T>(DynamoDbTableInfo tableInfo, List<AttributeValue> pkValues)
        {
            var result = new List<T>();

            var keys = pkValues
                .Distinct()
                .Select(pk => new Dictionary<string, AttributeValue>
                {
                    { tableInfo.PK, pk }
                })
                .ToList();

            while (keys.Any())
            {
                var batchKeys = keys.Take(100).ToList();
                keys = keys.Skip(100).ToList();

                var request = new BatchGetItemRequest
                {
                    RequestItems = new Dictionary<string, KeysAndAttributes>
                    {
                        {
                            tableInfo.TableName,
                            new KeysAndAttributes { Keys = batchKeys }
                        }
                    }
                };

                BatchGetItemResponse response;
                do
                {
                    response = await _dynamoDbClient.BatchGetItemAsync(request);

                    if (response.Responses.TryGetValue(tableInfo.TableName, out var items))
                    {
                        result.AddRange(items.Select(item =>
                            _dynamoDBContext.FromDocument<T>(
                                Document.FromAttributeMap(item))));
                    }

                    request.RequestItems = response.UnprocessedKeys;

                } while (response.UnprocessedKeys != null && response.UnprocessedKeys.Count > 0);
            }

            return result;
        }

        public async Task<List<T>> GetByPkAsync<T>(DynamoDbTableInfo tableInfo, string pkValue)
        {
            var results = new List<T>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    KeyConditionExpression = "#pk = :v_pk",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#pk", tableInfo.PK },
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        {":v_pk", new AttributeValue { S = pkValue }}
                    },
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                results.AddRange(
                    response.Items.Select(item =>
                        _dynamoDBContext.FromDocument<T>(Document.FromAttributeMap(item))
                    )
                );

                lastEvaluatedKey = response.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return results;
        }

        public async Task<List<T>> GetByPkAsyncV2<T>(DynamoDbTableInfo tableInfo, AttributeValue pkValue)
        {
            var results = new List<T>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    KeyConditionExpression = "#pk = :v_pk",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#pk", tableInfo.PK },
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        {":v_pk", pkValue }
                    },
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                results.AddRange(
                    response.Items.Select(item =>
                        _dynamoDBContext.FromDocument<T>(Document.FromAttributeMap(item))
                    )
                );

                lastEvaluatedKey = response.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return results;
        }

        public async Task<bool> DeleteByPKAndSKAsync(DynamoDbTableInfo tableInfo, AttributeValue pkValue, AttributeValue skValue)
        {
            var request = new DeleteItemRequest
            {
                TableName = tableInfo.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { tableInfo.PK , pkValue},
                    { tableInfo.SK, skValue }
                }
            };

            var response = await _dynamoDbClient.DeleteItemAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<int> BatchDeleteByPKAndSKAsync(DynamoDbTableInfo tableInfo, IEnumerable<(AttributeValue pkValue, AttributeValue skValue)> keys)
        {
            int deletedCount = 0;

            foreach (var chunk in keys.Chunk(25))
            {
                var batchRequest = new BatchWriteItemRequest
                {
                    RequestItems = new Dictionary<string, List<WriteRequest>>
                    {
                        {
                            tableInfo.TableName,
                            chunk.Select(k => new WriteRequest
                            {
                                DeleteRequest = new DeleteRequest
                                {
                                    Key = new Dictionary<string, AttributeValue>
                                    {
                                        { tableInfo.PK, k.pkValue },
                                        { tableInfo.SK, k.skValue }
                                    }
                                }
                            }).ToList()
                        }
                    }
                };

                var response = await _dynamoDbClient.BatchWriteItemAsync(batchRequest);

                // [BUG / CODE SMELL] Tiềm ẩn lặp vô hạn nếu UnprocessedItems không giảm bớt và không có cơ chế retry / delay
                while (response.UnprocessedItems.Count > 0)
                {
                    response = await _dynamoDbClient.BatchWriteItemAsync(
                        new BatchWriteItemRequest
                        {
                            RequestItems = response.UnprocessedItems
                        });
                }

                deletedCount += chunk.Count();
            }

            return deletedCount;
        }

        public async Task<bool> UpdateItemAsync<T>(DynamoDbTableInfo tableInfo, T item)
        {
            try
            {
                AttributeValue pkValue = new AttributeValue();
                AttributeValue skValue = new AttributeValue();
                var map = new Dictionary<string, AttributeValue>();
                
                // [CODE SMELL] Biến `index` được khai báo, tăng giá trị nhưng không dùng tới
                int index = 0;

                foreach (var prop in typeof(T).GetProperties())
                {
                    index = index + 1;

                    if (Attribute.IsDefined(prop, typeof(DynamoDBIgnoreAttribute)))
                        continue;

                    if (Attribute.IsDefined(prop, typeof(DynamoDBHashKeyAttribute)))
                    {
                        var valuePK = prop.GetValue(item);
                        pkValue = GetAttributeValue(valuePK);
                        continue;
                    }

                    if (Attribute.IsDefined(prop, typeof(DynamoDBRangeKeyAttribute)))
                    {
                        var valueSK = prop.GetValue(item);
                        skValue = GetAttributeValue(valueSK);
                        continue;
                    }

                    var value = prop.GetValue(item);
                    var attr = prop.GetCustomAttribute<DynamoDBPropertyAttribute>();
                    var attrName = attr?.AttributeName ?? prop.Name;
                    map[attrName] = GetAttributeValue(value);
                }

                var result = await this.UpdateItemAtrAsync(tableInfo, pkValue, skValue, map);
                return true;
            }
            catch (Exception ex)
            {
                CommonLogger.Error(ex);
                // [CODE SMELL] Rethrowing Exception làm mất thông tin stack trace gốc thay vì dùng `throw;`
                throw new Exception(ex.Message, ex);
            }
        }

        public async Task<int> BatchUpdateAsync<T>(DynamoDbTableInfo tableInfo, IEnumerable<T> items)
        {
            int updatedCount = 0;

            foreach (var item in items)
            {
                await UpdateItemAsync(tableInfo, item);
                updatedCount++;
            }

            return updatedCount;
        }

        private static AttributeValue GetAttributeValue(object value)
        {
            // [CODE SMELL] Switch case không bao quát giá trị null ban đầu
            switch (value)
            {
                case string s:
                    return new AttributeValue { S = s };

                case int i:
                case long l:
                case double d:
                case float f:
                case decimal m:
                    return new AttributeValue { N = value.ToString() };

                case bool b:
                    return new AttributeValue { BOOL = b };

                case DateTime dt:
                    return new AttributeValue
                    {
                        S = dt.ToUniversalTime().ToString("o")
                    };

                case List<string> list:
                    return new AttributeValue
                    {
                        L = list.Select(x => new AttributeValue { S = x }).ToList()
                    };

                case HashSet<string> list:
                    return new AttributeValue
                    {
                        L = list.Select(x => new AttributeValue { S = x }).ToList()
                    };

                case List<int> list:
                    return new AttributeValue
                    {
                        L = list.Select(x => new AttributeValue { N = x.ToString() }).ToList()
                    };

                case HashSet<int> list:
                    return new AttributeValue
                    {
                        L = list.Select(x => new AttributeValue { N = x.ToString() }).ToList()
                    };

                case List<object> list:
                    return new AttributeValue
                    {
                        L = list.Select(x => new AttributeValue { S = JsonSerializer.Serialize(x) }).ToList()
                    };

                default:
                    return new AttributeValue { S = (value ?? "").ToString() };
            }
        }

        public async Task<bool> UpdateItemAtrAsync(DynamoDbTableInfo tableInfo, AttributeValue pkValue, AttributeValue skValue, Dictionary<string, AttributeValue> attributesToUpdate)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                { tableInfo.PK, pkValue },
                { tableInfo.SK,  skValue}
            };

            // [CODE SMELL] Nối chuỗi bằng String Concatenation thay vì dùng StringBuilder hoặc string.Join trong vòng lặp
            string updateExpressionStr = "SET ";
            var expressionAttributeNames = new Dictionary<string, string>();
            var expressionAttributeValues = new Dictionary<string, AttributeValue>();

            int index = 0;
            foreach (var kvp in attributesToUpdate)
            {
                string attrNameKey = $"#attr{index}";
                string attrValueKey = $":val{index}";

                updateExpressionStr += $"{attrNameKey} = {attrValueKey}, ";
                expressionAttributeNames[attrNameKey] = kvp.Key;
                expressionAttributeValues[attrValueKey] = kvp.Value;

                index++;
            }

            // [BUG] Dấu phẩy dư thừa ở cuối chuỗi updateExpressionStr sẽ gây lỗi cú pháp DynamoDB
            updateExpressionStr = updateExpressionStr.TrimEnd(',', ' ');

            var request = new UpdateItemRequest
            {
                TableName = tableInfo.TableName,
                Key = key,
                UpdateExpression = updateExpressionStr,
                ExpressionAttributeNames = expressionAttributeNames,
                ExpressionAttributeValues = expressionAttributeValues,
                ReturnValues = "UPDATED_NEW"
            };

            var response = await _dynamoDbClient.UpdateItemAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<int> DeleteByPkAsync(DynamoDbTableInfo tableInfo, string pkValue)
        {
            int deletedCount = 0;
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var queryRequest = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    KeyConditionExpression = "#pk = :v_pk",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#pk", tableInfo.PK }
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_pk", new AttributeValue { S = pkValue } }
                    },
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var queryResponse = await _dynamoDbClient.QueryAsync(queryRequest);

                if (queryResponse.Items.Count > 0)
                {
                    foreach (var chunk in queryResponse.Items.Chunk(25))
                    {
                        var writeRequests = chunk.Select(item => new WriteRequest
                        {
                            DeleteRequest = new DeleteRequest
                            {
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    { tableInfo.PK, item[tableInfo.PK] },
                                    { tableInfo.SK, item[tableInfo.SK] }
                                }
                            }
                        }).ToList();

                        var batchRequest = new BatchWriteItemRequest
                        {
                            RequestItems = new Dictionary<string, List<WriteRequest>>
                            {
                                { tableInfo.TableName, writeRequests }
                            }
                        };

                        var response = await _dynamoDbClient.BatchWriteItemAsync(batchRequest);

                        while (response.UnprocessedItems.Count > 0)
                        {
                            response = await _dynamoDbClient.BatchWriteItemAsync(new BatchWriteItemRequest
                            {
                                RequestItems = response.UnprocessedItems
                            });
                        }

                        deletedCount += chunk.Count();
                    }
                }

                lastEvaluatedKey = queryResponse.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return deletedCount;
        }

        public async Task<List<T>> GetByPkAndSdateGreaterEdateLessAsync<T>(DynamoDbTableInfo tableInfo, string pkValue, long edate, long sdate)
        {
            var results = new List<T>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    IndexName = "companyId-edate-index",
                    KeyConditionExpression = "#pk = :v_pk AND #edate > :v_sdate",
                    FilterExpression = "#sdate < :v_edate",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#pk", tableInfo.PK },
                        { "#edate", "edate" },
                        { "#sdate", "sdate" }
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_pk", new AttributeValue { S = pkValue } },
                        { ":v_sdate", new AttributeValue { N = sdate.ToString() } },
                        { ":v_edate", new AttributeValue { N = edate.ToString() } }
                    },
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                results.AddRange(
                    response.Items.Select(item =>
                        _dynamoDBContext.FromDocument<T>(
                            Document.FromAttributeMap(item)
                        )
                    )
                );

                lastEvaluatedKey = response.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return results;
        }

        public async Task<List<AssignTag>> GetItemWithMaxSdateLessThanAsync(DynamoDbTableInfo tableInfo, string pkValue, long sdateValue)
        {
            var results = new List<AssignTag>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    IndexName = "tagid-edate-index",
                    KeyConditionExpression = "#pk = :v_pk",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#pk", "tagid" },
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_pk", new AttributeValue { S = pkValue } },
                    },
                    ScanIndexForward = false,
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                results.AddRange(
                    response.Items.Select(item =>
                        _dynamoDBContext.FromDocument<AssignTag>(
                            Document.FromAttributeMap(item)
                        )
                    )
                );

                if (results.Any(x => x.Sdate < sdateValue))
                {
                    return results;
                }

                lastEvaluatedKey = response.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return results;
        }

        public async Task<Models.UpdateConfig> GetConfigLastUpdate(DynamoDbTableInfo tableInfo, string mapId)
        {
            var request = new QueryRequest
            {
                TableName = tableInfo.TableName,
                IndexName = "mid-upddate-index",
                KeyConditionExpression = "#pk = :v_pk",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#pk", "mid" },
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":v_pk", new AttributeValue { S = mapId } },
                },
                ScanIndexForward = false,
                Limit = 1
            };

            var response = await _dynamoDbClient.QueryAsync(request);

            if (response.Items == null || response.Items.Count == 0)
                return default;

            var document = Document.FromAttributeMap(response.Items[0]);
            return _dynamoDBContext.FromDocument<UpdateConfig>(document);
        }

        public async Task<List<T>> GetAllPkByIndexAsync<T>(DynamoDbTableInfo tableInfo, string indexName, string gsiPKName, AttributeValue pkvalue)
        {
            var pkList = new List<T>();
            Dictionary<string, AttributeValue> lastEvaluatedKey = null;

            do
            {
                var request = new QueryRequest
                {
                    TableName = tableInfo.TableName,
                    IndexName = indexName,
                    KeyConditionExpression = "#GsiPK = :v_GsiPK",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#GsiPK", gsiPKName }
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_GsiPK", pkvalue }
                    },
                    ExclusiveStartKey = lastEvaluatedKey
                };

                var response = await _dynamoDbClient.QueryAsync(request);

                foreach (var item in response.Items)
                {
                    if (item.ContainsKey(gsiPKName))
                    {
                        var pkAttr = item[gsiPKName];

                        if (typeof(T) == typeof(string) && pkAttr.S != null)
                            pkList.Add((T)(object)pkAttr.S);
                        else if (typeof(T) == typeof(int) && pkAttr.N != null && int.TryParse(pkAttr.N, out var intVal))
                            pkList.Add((T)(object)intVal);
                        else if (typeof(T) == typeof(long) && pkAttr.N != null && long.TryParse(pkAttr.N, out var longVal))
                            pkList.Add((T)(object)longVal);
                        else
                            pkList.Add(_dynamoDBContext.FromDocument<T>(Document.FromAttributeMap(item)));
                    }
                }

                lastEvaluatedKey = response.LastEvaluatedKey;

            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return pkList;
        }
    }
}