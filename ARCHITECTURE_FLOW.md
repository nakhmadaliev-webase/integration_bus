# Integration Bus Architecture Flow

## Overall System Architecture with External Integration

```mermaid
graph TB
    subgraph "Internal Microservices"
        subgraph "Order Service"
            A1[Order Controller]
            A2[Order Service]
            A3[Order Repository]
            A4[OrderCreatedEventHandler]
            A5[Internal EventBus]
        end
        
        subgraph "Payment Service"
            B1[Payment Controller]
            B2[Payment Service]
            B3[Payment Repository]
            B4[PaymentProcessedEventHandler]
            B5[Internal EventBus]
        end
        
        subgraph "Notification Service"
            C1[Notification Controller]
            C2[Notification Service]
            C3[Notification Repository]
            C4[NotificationEventHandler]
            C5[Internal EventBus]
        end
    end
    
    subgraph "Integration Layer"
        subgraph "Internal Message Broker"
            IMB[(RabbitMQ / In-Memory)]
        end
        
        subgraph "External Integration Bus"
            EEB[External EventBus]
            WHR[Webhook Receiver]
            PGA[Payment Gateway Adapter]
            NSA[Notification Service Adapter]
        end
    end
    
    subgraph "External Systems"
        subgraph "Payment Gateway"
            PG[Payment API]
            PGWH[Payment Webhooks]
        end
        
        subgraph "Email Service"
            ES[Email API]
            ESWH[Email Webhooks]
        end
        
        subgraph "SMS Provider"
            SMS[SMS API]
            SMSWH[SMS Webhooks]
        end
    end
    
    %% Internal service connections
    A1 --> A2
    A2 --> A3
    A2 --> A5
    A5 --> IMB
    IMB --> B4
    IMB --> C4
    
    B1 --> B2
    B2 --> B3
    B2 --> B5
    B5 --> IMB
    IMB --> A4
    
    C1 --> C2
    C2 --> C3
    C2 --> C5
    C5 --> IMB
    
    %% External integration connections
    A5 --> EEB
    B5 --> EEB
    C5 --> EEB
    
    EEB --> PGA
    EEB --> NSA
    
    PGA --> PG
    NSA --> ES
    NSA --> SMS
    
    %% Webhook connections
    PGWH --> WHR
    ESWH --> WHR
    SMSWH --> WHR
    WHR --> IMB
```

## Event Publishing Flow with External Integration

```mermaid
sequenceDiagram
    participant Client
    participant OrderController
    participant OrderService
    participant InternalEventBus
    participant MessageBroker
    participant ExternalEventBus
    participant PaymentAdapter
    participant PaymentGateway
    participant WebhookReceiver
    
    Client->>OrderController: POST /api/orders
    OrderController->>OrderService: CreateOrder(request)
    OrderService->>OrderService: Create Order Entity
    OrderService->>OrderService: Save to Database
    
    OrderService->>InternalEventBus: PublishAsync(OrderCreatedEvent)
    InternalEventBus->>MessageBroker: Serialize & Send Message
    
    OrderController-->>Client: 201 Created (Order Response)
    
    MessageBroker->>ExternalEventBus: OrderCreatedEvent
    ExternalEventBus->>PaymentAdapter: Process Payment Request
    PaymentAdapter->>PaymentGateway: HTTP POST /api/payments/process
    
    Note over PaymentGateway: External payment processing
    
    PaymentGateway-->>PaymentAdapter: Payment Response
    PaymentAdapter-->>ExternalEventBus: Success/Failure
    
    PaymentGateway->>WebhookReceiver: Webhook: PaymentCompletedEvent
    WebhookReceiver->>WebhookReceiver: Validate Signature
    WebhookReceiver->>InternalEventBus: PublishAsync(PaymentCompletedEvent)
    InternalEventBus->>MessageBroker: Internal Event Distribution
```

## Class Hierarchy & Dependencies with External Integration

```mermaid
classDiagram
    class IIntegrationEvent {
        <<interface>>
        +Guid Id
        +DateTime OccurredOn
        +string EventType
    }
    
    class IntegrationEvent {
        <<abstract>>
        +Guid Id
        +DateTime OccurredOn
        +string EventType
    }
    
    class IExternalIntegrationEvent {
        <<interface>>
        +string ExternalSystemId
        +string ExternalEventId
        +Dictionary Metadata
    }
    
    class ExternalIntegrationEvent {
        <<abstract>>
        +string ExternalSystemId
        +string ExternalEventId
        +Dictionary Metadata
    }
    
    class OrderCreatedEvent {
        +int OrderId
        +string CustomerName
        +decimal TotalAmount
        +DateTime OrderDate
    }
    
    class PaymentCompletedEvent {
        +string TransactionId
        +int OrderId
        +decimal Amount
        +DateTime CompletedAt
    }
    
    class IEventBus {
        <<interface>>
        +PublishAsync() Task
        +Subscribe() void
    }
    
    class IExternalEventBus {
        <<interface>>
        +PublishToExternalSystemAsync() Task
        +RequestFromExternalSystemAsync() Task
        +RegisterExternalSystem() void
    }
    
    class IExternalSystemClient {
        <<interface>>
        +string SystemId
        +SendAsync() Task
        +HealthCheckAsync() Task
    }
    
    class HttpExternalSystemClient {
        -HttpClient _httpClient
        -ExternalSystemOptions _options
        +SendAsync() Task
        +GetAsync() Task
        +HealthCheckAsync() Task
    }
    
    class ResilientHttpExternalSystemClient {
        -HttpExternalSystemClient _innerClient
        -ResiliencePolicy _resiliencePolicy
        -CircuitBreakerPolicy _circuitBreakerPolicy
        +SendAsync() Task
    }
    
    class PaymentGatewayAdapter {
        -IExternalEventBus _externalEventBus
        +ProcessPaymentAsync() Task
        +RefundPaymentAsync() Task
        +GetPaymentStatusAsync() Task
    }
    
    class IWebhookReceiver {
        <<interface>>
        +ValidateWebhookAsync() Task
        +ProcessWebhookAsync() Task
    }
    
    IIntegrationEvent <|-- IntegrationEvent
    IIntegrationEvent <|-- IExternalIntegrationEvent
    IntegrationEvent <|-- ExternalIntegrationEvent
    IntegrationEvent <|-- OrderCreatedEvent
    ExternalIntegrationEvent <|-- PaymentCompletedEvent
    IExternalSystemClient <|-- HttpExternalSystemClient
    IExternalSystemClient <|-- ResilientHttpExternalSystemClient
    HttpExternalSystemClient <|-- ResilientHttpExternalSystemClient
    PaymentGatewayAdapter --> IExternalEventBus
    IExternalEventBus --> IExternalSystemClient
```

## Event Subscription & Handling Flow

```mermaid
flowchart TD
    A[Application Startup] --> B[Register Services]
    B --> C[Build Service Provider]
    C --> D[Get IEventBus Instance]
    D --> E[Subscribe to Events]
    
    E --> F{Event Subscription}
    F --> G["eventBus.Subscribe OrderCreatedEvent"]
    F --> H["eventBus.Subscribe PaymentProcessedEvent"]
    F --> I["eventBus.Subscribe InventoryUpdatedEvent"]
    
    G --> J[Add to SubscriptionsManager]
    H --> J
    I --> J
    
    J --> K[Start Message Consumers]
    K --> L[Application Ready]
    
    subgraph "Runtime Event Handling"
        M[Event Published] --> N[Message Broker Receives]
        N --> O[Find Registered Handlers]
        O --> P[Create Service Scope]
        P --> Q[Resolve Handler from DI]
        Q --> R[Invoke Handler HandleAsync]
        R --> S[Complete Processing]
    end
    
    L --> M
```

## Dependency Injection Container Flow

```mermaid
graph TD
    subgraph "Service Registration"
        A[Program.cs] --> B[AddInMemoryEventBus / AddRabbitMQEventBus]
        B --> C[AddIntegrationBusCore]
        C --> D[Register IEventBusSubscriptionsManager]
        C --> E[Register IEventBus Implementation]
        
        A --> F[AddIntegrationEventHandler<Handler, Event>]
        F --> G[Register Handler as Transient]
    end
    
    subgraph "Runtime Resolution"
        H[Event Publishing] --> I[Resolve IEventBus]
        I --> J[InMemoryEventBus / RabbitMQEventBus]
        
        K[Event Handling] --> L[Create Service Scope]
        L --> M[Resolve Handler Type]
        M --> N[Invoke Handler HandleAsync]
    end
    
    E --> I
    G --> M
```

## Message Flow Architecture with External Integration

```mermaid
flowchart LR
    subgraph "Internal Microservices"
        subgraph "Publisher Microservice"
            P1[Controller] --> P2[Business Logic]
            P2 --> P3[Domain Event]
            P3 --> P4[Internal Event Bus]
        end
        
        subgraph "Subscriber Microservice"
            C1[Event Handler] --> C2[Business Logic]
            C2 --> C3[Database Update]
        end
    end
    
    subgraph "Integration Layer"
        subgraph "Message Infrastructure"
            MB1[(Internal Message Broker)]
            Q1[Queue/Exchange]
            S1[Serialization]
            S2[Deserialization]
        end
        
        subgraph "External Integration"
            EEB[External Event Bus]
            PGA[Payment Adapter]
            NSA[Notification Adapter]
            WHR[Webhook Receiver]
        end
    end
    
    subgraph "External Systems"
        subgraph "Payment Gateway"
            PG[Payment API]
            PGWH[Payment Webhooks]
        end
        
        subgraph "Notification Service"
            NS[Email/SMS API]
            NSWH[Delivery Webhooks]
        end
    end
    
    %% Internal flow
    P4 --> S1
    S1 --> MB1
    MB1 --> Q1
    Q1 --> S2
    S2 --> C1
    
    %% External integration flow
    P4 --> EEB
    EEB --> PGA
    EEB --> NSA
    
    PGA --> PG
    NSA --> NS
    
    %% Webhook flow
    PGWH --> WHR
    NSWH --> WHR
    WHR --> MB1
```

## Error Handling & Resilience Flow

```mermaid
sequenceDiagram
    participant EventBus
    participant MessageBroker
    participant EventHandler
    participant DeadLetterQueue
    participant Logger
    
    EventBus->>MessageBroker: Publish Event
    
    loop Retry Logic
        MessageBroker->>EventHandler: Deliver Event
        
        alt Success
            EventHandler->>EventHandler: Process Successfully
            EventHandler-->>MessageBroker: ACK
        else Transient Error
            EventHandler->>Logger: Log Warning
            EventHandler-->>MessageBroker: NACK (Retry)
            Note over MessageBroker: Wait & Retry
        else Permanent Error
            EventHandler->>Logger: Log Error
            EventHandler->>DeadLetterQueue: Send to DLQ
            EventHandler-->>MessageBroker: ACK (Don't Retry)
        end
    end
    
    alt Max Retries Exceeded
        MessageBroker->>DeadLetterQueue: Send to DLQ
        MessageBroker->>Logger: Log Critical Error
    end
```

## Saga Pattern Implementation Flow

```mermaid
stateDiagram-v2
    [*] --> OrderCreated
    
    OrderCreated --> ProcessPayment : OrderCreatedEvent
    ProcessPayment --> PaymentSuccess : PaymentProcessedEvent(Success)
    ProcessPayment --> PaymentFailed : PaymentProcessedEvent(Failed)
    
    PaymentSuccess --> ReserveInventory : PaymentSuccessfulEvent
    ReserveInventory --> InventoryReserved : InventoryReservedEvent
    ReserveInventory --> InventoryFailed : InventoryNotAvailableEvent
    
    InventoryReserved --> ConfirmOrder : InventoryReservedEvent
    ConfirmOrder --> OrderCompleted : OrderConfirmedEvent
    
    PaymentFailed --> CancelOrder : PaymentFailedEvent
    InventoryFailed --> RefundPayment : InventoryFailedEvent
    RefundPayment --> CancelOrder : PaymentRefundedEvent
    CancelOrder --> OrderCancelled : OrderCancelledEvent
    
    OrderCompleted --> [*]
    OrderCancelled --> [*]
```

## Performance & Scalability Flow

```mermaid
graph TD
    subgraph "Load Balancer"
        LB[Load Balancer]
    end
    
    subgraph "Microservice Instances"
        MS1[Order Service Instance 1]
        MS2[Order Service Instance 2]
        MS3[Order Service Instance 3]
    end
    
    subgraph "Message Broker Cluster"
        MB1[(RabbitMQ Node 1)]
        MB2[(RabbitMQ Node 2)]
        MB3[(RabbitMQ Node 3)]
    end
    
    subgraph "Consumer Instances"
        C1[Payment Service Instance 1]
        C2[Payment Service Instance 2]
        C3[Inventory Service Instance 1]
        C4[Inventory Service Instance 2]
    end
    
    LB --> MS1
    LB --> MS2
    LB --> MS3
    
    MS1 --> MB1
    MS2 --> MB2
    MS3 --> MB3
    
    MB1 --> C1
    MB1 --> C3
    MB2 --> C2
    MB2 --> C4
    MB3 --> C1
    MB3 --> C3
```

## Development vs Production Flow with External Integration

```mermaid
graph TB
    subgraph "Development Environment"
        DEV1[In-Memory Event Bus]
        DEV2[Single Process]
        DEV3[Immediate Delivery]
        DEV4[Console Logging]
        DEV5[Mock External Systems]
        DEV6[Test Webhooks]
    end
    
    subgraph "Production Environment"
        PROD1[RabbitMQ Event Bus]
        PROD2[Distributed Services]
        PROD3[Persistent Queues]
        PROD4[Structured Logging]
        PROD5[Monitoring & Metrics]
        PROD6[Circuit Breakers]
        PROD7[Dead Letter Queues]
        PROD8[Real External APIs]
        PROD9[Webhook Security]
        PROD10[Retry Policies]
    end
    
    subgraph "External Integration"
        EXT1[HTTP Clients]
        EXT2[Webhook Receivers]
        EXT3[System Adapters]
    end
    
    subgraph "Configuration"
        CONFIG[appsettings.json]
        ENV[Environment Variables]
        SECRETS[Azure Key Vault / Secrets]
    end
    
    CONFIG --> DEV1
    CONFIG --> PROD1
    ENV --> PROD1
    SECRETS --> PROD8
    
    DEV1 --> DEV2
    DEV2 --> DEV3
    DEV3 --> DEV4
    DEV4 --> DEV5
    DEV5 --> DEV6
    
    PROD1 --> PROD2
    PROD2 --> PROD3
    PROD3 --> PROD4
    PROD4 --> PROD5
    PROD5 --> PROD6
    PROD6 --> PROD7
    PROD7 --> PROD8
    PROD8 --> PROD9
    PROD9 --> PROD10
    
    EXT1 --> DEV5
    EXT1 --> PROD8
    EXT2 --> DEV6
    EXT2 --> PROD9
    EXT3 --> DEV5
    EXT3 --> PROD8
```

## External System Integration Flow with Resilience

```mermaid
graph TD
    subgraph "Internal Services"
        IS[Internal Service]
        IEB[Internal Event Bus]
        IH[Internal Event Handler]
    end
    
    subgraph "External Integration Layer"
        EEB[External Event Bus]
        PGA[Payment Gateway Adapter]
        NSA[Notification Service Adapter]
        WHR[Webhook Receiver]
        
        subgraph "Resilience Policies"
            CB[Circuit Breaker]
            RP[Retry Policy]
            TO[Timeout Policy]
        end
        
        subgraph "HTTP Clients"
            HTTP1[HTTP Client - Payment]
            HTTP2[HTTP Client - Notification]
        end
    end
    
    subgraph "External Systems"
        subgraph "Payment Gateway"
            PGS[Payment API]
            PGWH[Payment Webhooks]
        end
        
        subgraph "Notification Services"
            ESS[Email Service API]
            ESWH[Email Webhooks]
            SMS[SMS Service API]
            SMSWH[SMS Webhooks]
        end
    end
    
    %% Internal to External flow
    IS --> IEB
    IEB --> EEB
    EEB --> PGA
    EEB --> NSA
    
    %% Payment flow with resilience
    PGA --> CB
    CB --> RP
    RP --> TO
    TO --> HTTP1
    HTTP1 --> PGS
    
    %% Notification flow
    NSA --> HTTP2
    HTTP2 --> ESS
    HTTP2 --> SMS
    
    %% Webhook return flow
    PGWH --> WHR
    ESWH --> WHR
    SMSWH --> WHR
    WHR --> IEB
    IEB --> IH
    
    %% Error handling
    CB -.-> EEB
    RP -.-> EEB
    
    style CB fill:#ff9999
    style RP fill:#ffcc99
    style TO fill:#99ccff
    style WHR fill:#99ff99
```