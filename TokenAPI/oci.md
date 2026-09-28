docker login -u 'nreio7apu7z0/minh_nh@care-connect.vn' -p 'vmSX9YV5D(;cs:yeI4P]' ap-tokyo-1.ocir.io


# build and push token-api Docker image
docker buildx build --platform linux/amd64 -t develop-token-api . --load

docker tag develop-token-api:latest ap-tokyo-1.ocir.io/nreio7apu7z0/develop-token-api:latest

docker push ap-tokyo-1.ocir.io/nreio7apu7z0/develop-token-api:latest


# Cluster id : ocid1.cluster.oc1.ap-tokyo-1.aaaaaaaadafjnd5ktgkr6dlsa2vimnxjn63dtoz6d46nhj64vcrmn3gtim2q
oci ce cluster create-kubeconfig --cluster-id ocid1.cluster.oc1.ap-tokyo-1.aaaaaaaadafjnd5ktgkr6dlsa2vimnxjn63dtoz6d46nhj64vcrmn3gtim2q --file $HOME/.kube/config --region ap-tokyo-1 --token-version 2.0.0  --kube-endpoint PRIVATE_ENDPOINT

kubectl create secret docker-registry ocir-secret \
  --docker-server=ap-tokyo-1.ocir.io \
  --docker-username='nreio7apu7z0/minh_nh@care-connect.vn' \
  --docker-password='vmSX9YV5D(;cs:yeI4P]'

# Tạo secret chứa file config và key cho Oracle NoSQL SDK

kubectl create secret generic oci-config-secret \
  --from-file=config=.oci/config \
  --from-file=oci_api_key.pem=.oci/oci_api_key.pem
  
# kubectl delete secret oci-config-secret

curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash

# Check existing helm releases
helm list -n ingress-nginx

# Uninstall nginx-ingress if exists (ignore error if not found)
helm uninstall nginx-ingress  -n ingress-nginx
helm uninstall nginx-ingress -n ingress-nginx || echo "nginx-ingress release not found, continuing..."

# Add helm repo and update
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo update

kubectl get events -n ingress-nginx --sort-by='.lastTimestamp'


# Install nginx-ingress with Oracle Cloud specific configurations and proper defined tags
 helm install nginx-ingress ingress-nginx/ingress-nginx   --namespace ingress-nginx --create-namespace   -f public-nginx-values.yaml

kubectl delete -f token-api-deployment.yaml
kubectl delete -f token-api-ingress.yaml


kubectl apply -f token-api-deployment.yaml
kubectl apply -f token-api-ingress.yaml

kubectl rollout restart deployment token-api   
kubectl rollout restart deployment token-api-ingress

kubectl get jobs -n token-api

# NODE-POOL-ID
oci ce node-pool get --node-pool-id ocid1.nodepool.oc1.ap-tokyo-1.aaaaaaaaqybhkhvz6ud2rszxtwzve5bya65igpz7rsywk6axanmbpg65d6fq \
  --query "data.nodes[*].{Name:\"display-name\", State:\"lifecycle-state\", ID:id}" \
  --output table

kubectl get nodes

# Check ingress service status and wait for external IP
kubectl get svc -n ingress-nginx -w --timeout=300s

# Check ingress controller pods
kubectl get pods -n ingress-nginx

# Check events for troubleshooting
kubectl get events -n ingress-nginx --sort-by='.lastTimestamp'






# CHECK DEPLOYMENT STATUS #########################

# 1. Check deployment status:
# Check if deployment is ready
kubectl get deployments
# View deployment details
kubectl describe deployment token-api

# 2. Check pods:
# View running pods
kubectl get pods
# View pod logs (for debugging if there are errors)
kubectl logs -l app=token-api

# 3. Check ingress:
# Check if ingress has been created
kubectl get ingress
# View ingress details
kubectl describe ingress token-api-ingress

# 4. Check service:
# Check service
kubectl get services
# Check if nginx ingress controller has external IP
kubectl get svc -n ingress-nginx