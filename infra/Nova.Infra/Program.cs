using Pulumi;
using Pulumi.Aws.S3;
using Pulumi.Aws.S3.Inputs;

return await Deployment.RunAsync(() =>
{
    Bucket bucket = new("chat-library-files");


    _ = new BucketPublicAccessBlock
    (
        name: "chat-library-files-public-access-block",
        args: new BucketPublicAccessBlockArgs
        {
            Bucket = bucket.Id,
            BlockPublicAcls = true,
            BlockPublicPolicy = true,
            IgnorePublicAcls = true,
            RestrictPublicBuckets = true,
        }
    );
    
    // Disable ACLs entirely; the bucket owner owns every object.
    _ = new BucketOwnershipControls
    (
        name: "chat-library-files-ownership",
        args: new BucketOwnershipControlsArgs
        {
            Bucket = bucket.Id,
            Rule = new BucketOwnershipControlsRuleArgs { ObjectOwnership = "BucketOwnerEnforced", },
        }
    );

    _ = new BucketServerSideEncryptionConfiguration
    (
        name: "chat-library-files-encryption",
        args: new Pulumi.Aws.S3.BucketServerSideEncryptionConfigurationArgs
        {
            Bucket = bucket.Id,
            Rules =
            {
                new BucketServerSideEncryptionConfigurationRuleArgs
                {
                    ApplyServerSideEncryptionByDefault =
                        new
                            BucketServerSideEncryptionConfigurationRuleApplyServerSideEncryptionByDefaultArgs
                            {
                                SseAlgorithm = "AES256",
                            },
                },
            },
        }
    );
    
    return new Dictionary<string, object?>
    {
        ["bucketName"] = bucket.BucketName,
        ["bucketArn"] = bucket.Arn,
    };
});