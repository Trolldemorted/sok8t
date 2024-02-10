```yaml
---
apiVersion: v1
kind: ServiceAccount
metadata:
  name: sok8t-sa

---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: sok8t-sa-role
rules:
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["create", "get", "list", "delete"]

---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: sok8t-sa-rb
subjects:
- kind: ServiceAccount
  name: sok8t-sa
roleRef:
  kind: Role
  name: sok8t-sa-role
  apiGroup: rbac.authorization.k8s.io

---
apiVersion: v1
kind: Pod
metadata:
  name: sok8t
  labels:
    app.kubernetes.io/name: sok8t
spec:
  containers:
  - name: sok8t
    image: ghcr.io/trolldemorted/sok8t/sok8t:nightly
    args: ["--localPort", "8000", "--targetPort", "80", "--namespace", "testns", "--image", "nginx"]
    ports:
    - containerPort: 8000
    imagePullPolicy: Always
  serviceAccountName: sok8t-sa

---
apiVersion: v1
kind: Service
metadata:
  name: sok8t
  labels:
    name: sok8t
spec:
  type: NodePort
  selector:
    app.kubernetes.io/name: sok8t
  ports:
    - port: 8000
      targetPort: 8000
      nodePort: 30000
```
