# ServerlessAPI

.NET 8 기반의 AWS Lambda Serverless API. DynamoDB를 활용한 책 관리 시스템(CRUD)과 JWT 인증을 구현했습니다.

## 프로젝트 구조

- `src/ServerlessAPI/` - 메인 WebAPI Lambda 함수 (.NET 8)
- `src/ServerlessAPI/Controllers/BooksController.cs` - 책 관리 엔드포인트 (GET, POST, PUT, DELETE)
- `src/ServerlessAPI/JwtValidator.cs` - JWT 토큰 검증 로직
- `src/ServerlessAPI/Repositories/` - DynamoDB 데이터 접근 계층
- `tests/` - 단위 테스트
- `template.yaml` - SAM IaC 설정 (Lambda, API Gateway, DynamoDB)

## 주요 기능

### 1. JWT 인증 및 역할 기반 권한 제어

- **방식**: HS256 대칭 암호화
- **검증**: 모든 API 엔드포인트에 JWT 필수
- **구현**: `JwtAuthorizerFunction.cs`에서 Bearer 토큰 추출 및 서명 검증
- **키**: 환경변수 `JWT_SECRET` (기본값: "your-secret-key-change-this-in-production")
- **역할 기반 제어**: JWT의 `role` claim을 기반으로 권한 제어

#### 역할 정의

| 역할  | GET | POST | PUT | DELETE |
| ----- | --- | ---- | --- | ------ |
| admin | ✅  | ✅   | ✅  | ✅     |
| user  | ✅  | ❌   | ❌  | ❌     |

#### JWT 토큰 예시

**Admin 토큰:**

```json
{
  "sub": "admin-user",
  "role": "admin",
  "exp": 1766581288
}
```

**User 토큰:**

```json
{
  "sub": "normal-user",
  "role": "user",
  "exp": 1766581288
}
```

### 2. DynamoDB 통합

- **테이블**: ServerlessAPIBookCatalog
- **파티션 키**: Id (String)
- **프로비저닝**: 읽기/쓰기 각 2 RCU/WCU
- **CRUD 작업**: 모두 지원

### 3. API 엔드포인트

```
GET    /api/books              - 책 목록 조회 (limit 파라미터 지원) [인증 필수]
GET    /api/books/{id}         - 특정 책 조회 [인증 필수]
POST   /api/books              - 책 추가 [Admin만 가능]
PUT    /api/books/{id}         - 책 수정 [Admin만 가능]
DELETE /api/books/{id}         - 책 삭제 [Admin만 가능]
```

#### 권한별 응답

- **GET**: HTTP 200 (모든 인증된 사용자)
- **POST/PUT/DELETE (Admin)**: HTTP 200/201 (성공)
- **POST/PUT/DELETE (User)**: HTTP 403 Forbidden
  ```json
  { "message": "Access denied: Only admins can create/update/delete books" }
  ```

## Lambda 성능 최적화

### 문제: stdout 경합으로 인한 데드락

AWS Lambda 환경에서 ASP.NET Core의 `ILogger`와 Lambda의 CloudWatch 로거가 동시에 stdout을 접근할 때 발생하는 경합(lock contention)으로 인해 요청이 타임아웃되는 이슈가 있었습니다.

**증상:**

- API 요청 → 503 Service Unavailable 반환
- 실제 처리는 5-11ms인데 타임아웃 발생
- Lambda 로그에는 에러 메시지 없음 (stdout 락에 의해 블로킹)

**근본 원인:**

```
ILogger (ASP.NET Core)
    ↓
  stdout 버퍼 (lock 획득)
    ↓
CloudWatch Logger (Lambda)
    ↓
stdout 버퍼 (lock 대기) ← 데드락 발생!
```

### 해결책: Console.Out.Flush()

```csharp
Console.WriteLine("Log message");
Console.Out.Flush();  // 버퍼를 즉시 비워서 lock 시간 최소화
```

**적용 범위:**

- `JwtValidator.cs`: 모든 검증 로그 다음에 Flush()
- `BooksController.cs`: 모든 요청/응답 로그 다음에Flush()
- `BookRepository.cs`: 모든 DynamoDB 작업 로그 다음에 Flush()

**효과:**

- stdout 버퍼가 자주 비워지므로 lock 점유 시간 단축
- 다른 로거도 빠르게 lock 획득 가능
- 요청 처리 속도 정상화 (5-11ms로 단축)

### 설정값

- **Lambda Timeout**: 60초 (실제 요청 처리: ~5-11ms)
- **API Gateway Timeout**: 30초 (30000ms)
- **추천사항**: ILogger DI는 유지하되, Console 기반 로깅과 Flush() 패턴 적용

## 배포 및 테스트

### 사전 요구사항

- AWS SAM CLI
- .NET 8.0 SDK
- Docker
- AWS CLI 설정 완료

### 배포 방법

```bash
# 빌드
sam build

# 배포 (처음)
sam deploy --guided

# 배포 (이후)
sam deploy
```

### 로컬 테스트

**Step 1: 환경변수 설정 (필수)**

```bash
export AWS_REGION=ap-southeast-2
export JWT_SECRET="your-secret-key-change-this-in-production"
```

**Step 2: 로컬 API 서버 시작 (dotnet run 권장)**

```bash
cd src/ServerlessAPI
dotnet run
```

또는 SAM 로컬 환경:

```bash
sam local start-api
```

**Step 3: JWT 토큰 생성 (Python)**

Python을 이용해 JWT 토큰을 생성합니다:

```bash
cat > /tmp/gen_tokens.py << 'EOF'
import base64
import json
import hmac
import hashlib
from datetime import datetime, timedelta

secret = "your-secret-key-change-this-in-production"

def create_jwt(sub, role):
    # Header
    header = {"alg": "HS256", "typ": "JWT"}
    header_encoded = base64.urlsafe_b64encode(json.dumps(header).encode()).rstrip(b'=').decode()

    # Payload
    now = datetime.utcnow()
    exp = int((now + timedelta(hours=1)).timestamp())
    payload = {"sub": sub, "role": role, "exp": exp}
    payload_encoded = base64.urlsafe_b64encode(json.dumps(payload).encode()).rstrip(b'=').decode()

    # Signature
    message = f"{header_encoded}.{payload_encoded}"
    signature = hmac.new(secret.encode(), message.encode(), hashlib.sha256).digest()
    signature_encoded = base64.urlsafe_b64encode(signature).rstrip(b'=').decode()

    return f"{message}.{signature_encoded}"

admin_token = create_jwt("admin-user", "admin")
user_token = create_jwt("normal-user", "user")

print("Admin Token (role: admin):")
print(admin_token)
print("\nUser Token (role: user):")
print(user_token)
EOF

python3 /tmp/gen_tokens.py
```

**Step 4: API 호출 테스트**

```bash
# Admin 토큰으로 책 생성 (성공 예상 - HTTP 201)
curl -X POST http://localhost:5000/api/books \
  -H "Authorization: Bearer <ADMIN_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"id":"550e8400-e29b-41d4-a716-446655440001","title":"Test Book","isbn":"123-4-56789-00-0","authors":["Author Name"]}' \
  -w "\nHTTP Status: %{http_code}\n"

# User 토큰으로 책 생성 시도 (거부 예상 - HTTP 403)
curl -X POST http://localhost:5000/api/books \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"id":"550e8400-e29b-41d4-a716-446655440002","title":"User Book","isbn":"234-5-67890-00-0","authors":["User Author"]}' \
  -w "\nHTTP Status: %{http_code}\n"

# Admin 토큰으로 책 조회 (성공 예상 - HTTP 200)
curl -H "Authorization: Bearer <ADMIN_TOKEN>" http://localhost:5000/api/books \
  -w "\nHTTP Status: %{http_code}\n"

# User 토큰으로 책 조회 (성공 예상 - HTTP 200)
curl -H "Authorization: Bearer <USER_TOKEN>" http://localhost:5000/api/books \
  -w "\nHTTP Status: %{http_code}\n"
```

**주의**: `AWS_REGION` 환경변수를 설정하지 않으면 로컬에서 DynamoDB 연결 시 실패합니다.

### AWS 배포 후 테스트

```bash
# JWT 토큰 생성
python3 /tmp/gen_tokens.py  # 위에서 생성한 스크립트 재사용

# API 엔드포인트 URL 확인
aws cloudformation describe-stacks \
  --stack-name ServerlessAPI \
  --region ap-southeast-2 \
  --query 'Stacks[0].Outputs[0].OutputValue' \
  --output text

# Admin으로 책 생성 (성공)
curl -X POST {API_GATEWAY_URL}/api/books \
  -H "Authorization: Bearer <ADMIN_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"id":"550e8400-e29b-41d4-a716-446655440001","title":"Cloud Book","isbn":"555-6-78901-00-0","authors":["Cloud Author"]}' \
  -w "\nHTTP Status: %{http_code}\n"

# User으로 책 생성 시도 (거부)
curl -X POST {API_GATEWAY_URL}/api/books \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"title":"User Book","isbn":"666-7-89012-00-0","authors":["User"]}' \
  -w "\nHTTP Status: %{http_code}\n"
```

## 로그 확인

```bash
# CloudWatch 로그 실시간 모니터링
aws logs tail /aws/lambda/ServerlessAPI-NetCodeWebAPIServerless-{FUNCTION_ID} --follow --region ap-southeast-2

# SAM CLI를 이용한 로그 조회
sam logs -n NetCodeWebAPIServerless --stack-name ServerlessAPI --tail
```

## 단위 테스트

```bash
dotnet test tests/ServerlessAPI.Tests/ServerlessAPI.Tests.csproj
```

## 리소스 정리

```bash
sam delete --stack-name ServerlessAPI
```

## 환경 변수

| 변수명         | 설명              | 기본값                                      |
| -------------- | ----------------- | ------------------------------------------- |
| `JWT_SECRET`   | JWT 서명 키       | "your-secret-key-change-this-in-production" |
| `SAMPLE_TABLE` | DynamoDB 테이블명 | "ServerlessAPIBookCatalog"                  |
| `AWS_REGION`   | AWS 리전          | "ap-southeast-2"                            |

## 참고 자료

- [AWS SAM 개발자 가이드](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/what-is-sam.html)
- [AWS Lambda .NET 런타임](https://docs.aws.amazon.com/lambda/latest/dg/lambda-dotnet.html)
- [Amazon DynamoDB 개발자 가이드](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/)
