# sok8t

socat, but for k8s.

```yaml
---
kind: Secret
apiVersion: v1
metadata:
  name: image-pull-secret
type: kubernetes.io/dockerconfigjson
data:
  .dockerconfigjson: eyJhdXRocyI6eyJyZWdpc3RyeS5naXRsYWIuY29tIjp7ImF1dGgiOiJUbTl3WlRwT2IxUnZhMlZ1Um05eWVXOTEifX19

---
apiVersion: v1
kind: ServiceAccount
metadata:
  name: sok8t-sa
imagePullSecrets:
- name: image-pull-secret

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
    args: ["8000", "80", "testns", "nginx"]
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
