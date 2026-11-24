# 02 Auth Security

### 02.01 (144 characters)

Implement ASP.NET Core Identity using EF Core and SQL Server. Support secure login, logout, password hashing, lockout, user accounts, and roles.

### 02.02 (152 characters)

Use secure HttpOnly cookies for browser login. Enforce HTTPS, CSRF protection, appropriate SameSite settings, expiration, and server-side authorization.

### 02.03 (163 characters)

Protect Web API independently using authenticated server-to-server requests. Validate identity and permissions on API endpoints; never trust client-supplied roles.

### 02.04 (127 characters)

Prepare API boundaries for future mobile OAuth2/OIDC with PKCE. Do not implement mobile UI or mobile authentication flow in V1.

### 02.05 (142 characters)

Create Manager and Staff roles. Managers approve sensitive operations. Enforce permissions in backend endpoints and services, not only the UI.

### 02.06 (136 characters)

Disable public registration. Provide secure initial Manager bootstrap, user administration, password reset, and security event auditing.

