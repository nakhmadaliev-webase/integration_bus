# Integration Bus Architecture Flow

## Overall System Architecture

```mermaid
graph TB
    subgraph "Microservice A (Order Service)"
        A1[Order Controller]
        A2[Order Service]
        A3[Order Repository]
        A4[OrderCreatedEventHandler]
        A5[IEventBus]
    end
    
    subgraph "Microservice B (Payment Service)"
        B1[Payment Controller]
        B2[Payment Service]
        B3[Payment Repository]
        B4[PaymentProcessedEventHandler]
        B5[IEventBus]
    end
    
    subgraph "Microservice C (Inventory Service)"
        C1[Inventory Controller]
        C2[Inventory Service]
        C3[Inventory Repository]
        C4[InventoryUpdatedEventHandler]
        C5[IEventBus]
    end
    
    subgraph "Message Broker"
        MB[(RabbitMQ / In-Memory)]
    end
    
    A1 --> A2
    A2 --> A3
    A2 --> A5
    A5 --> MB
    MB --> B4
    MB --> C4
    
    B1 --> B2
    B2 --> B3
    B2 --> B5
    B5 --> MB
    MB --> A4
    
    C1 --> C2
    C2 --> C3
    C2 --> C5
    C5 --> MB
    MB --> A4
    MB --> B4
```

## Event Publishing Flow

```mermaid
sequenceDiagram
    participant Client
    participant OrderController
    participant OrderService
    participant EventBus
    participant MessageBroker
    participant PaymentHandler
    participant InventoryHandler
    
    Client->>OrderController: POST /api/orders
    OrderController->>OrderService: CreateOrder(request)
    OrderService->>OrderService: Create Order Entity
    OrderService->>OrderService: Save to Database
    
    OrderService->>EventBus: PublishAsync(OrderCreatedEvent)
    EventBus->>MessageBroker: Serialize & Send Message
    
    OrderController-->>Client: 201 Created (Order Response)
    
    MessageBroker->>PaymentHandler: OrderCreatedEvent
    PaymentHandler->>PaymentHandler: Process Payment Logic
    
    MessageBroker->>InventoryHandler: OrderCreatedEvent
    InventoryHandler->>InventoryHandler: Update Inventory Logic
```

## Class Hierarchy & Dependencies

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
    
    class OrderCreatedEvent {
        +int OrderId
        +string CustomerName
        +decimal TotalAmount
        +DateTime OrderDate
    }
    
    class IIntegrationEventHandler~T~ {
        <<interface>>
        +HandleAsync(T event, CancellationToken token) Task
    }
    
    class IEventBus {
        <<interface>>
        +PublishAsync~T~(T event, CancellationToken token) Task
        +Subscribe~T,TH~() void
        +Unsubscribe~T,TH~() void
    }
    
    class InMemoryEventBus {
        -IServiceProvider _serviceProvider
        -IEventBusSubscriptionsManager _subscriptionsManager
        +PublishAsync~T~(T event, CancellationToken token) Task
        +Subscribe~T,TH~() void
    }
    
    class RabbitMQEventBus {
        -IConnection _connection
        -IModel _consumerChannel
        -RabbitMQEventBusOptions _options
        +PublishAsync~T~(T event, CancellationToken token) Task
        +Subscribe~T,TH~() void
    }
    
    class IEventBusSubscriptionsManager {
        <<interface>>
        +bool IsEmpty
        +event EventHandler~string~ OnEventRemoved
        +AddSubscription~T,TH~() void
        +GetHandlersForEvent(string eventName) IEnumerable~SubscriptionInfo~
    }
    
    IIntegrationEvent <|-- IntegrationEvent
    IntegrationEvent <|-- OrderCreatedEvent
    IEventBus <|-- InMemoryEventBus
    IEventBus <|-- RabbitMQEventBus
    InMemoryEventBus --> IEventBusSubscriptionsManager
    RabbitMQEventBus --> IEventBusSubscriptionsManager
```

## Event Subscription & Handling Flow

```mermaid
flowchart TD
    A[Application Startup] --> B[Register Services]
    B --> C[Build Service Provider]
    C --> D[Get IEventBus Instance]
    D --> E[Subscribe to Events]
    
    E --> F{Event Subscription}
    F --> G[eventBus.Subscribe<OrderCreatedEvent, OrderCreatedEventHandler>()]
    F --> H[eventBus.Subscribe<PaymentProcessedEvent, PaymentProcessedEventHandler>()]
    F --> I[eventBus.Subscribe<InventoryUpdatedEvent, InventoryUpdatedEventHandler>()]
    
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
        Q --> R[Invoke Handler.HandleAsync()]
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
        M --> N[Invoke Handler.HandleAsync]
    end
    
    E --> I
    G --> M
```

## Message Flow Architecture

```mermaid
flowchart LR
    subgraph "Publisher Microservice"
        P1[Controller] --> P2[Business Logic]
        P2 --> P3[Domain Event]
        P3 --> P4[Event Bus]
    end
    
    subgraph "Message Infrastructure"
        MB1[(Message Broker)]
        Q1[Queue/Exchange]
        S1[Serialization]
        S2[Deserialization]
    end
    
    subgraph "Subscriber Microservice 1"
        C1[Event Handler] --> C2[Business Logic]
        C2 --> C3[Database Update]
    end
    
    subgraph "Subscriber Microservice 2"
        D1[Event Handler] --> D2[Business Logic]
        D2 --> D3[External API Call]
    end
    
    P4 --> S1
    S1 --> MB1
    MB1 --> Q1
    Q1 --> S2
    S2 --> C1
    S2 --> D1
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

## Development vs Production Flow

```mermaid
graph TB
    subgraph "Development Environment"
        DEV1[In-Memory Event Bus]
        DEV2[Single Process]
        DEV3[Immediate Delivery]
        DEV4[Console Logging]
    end
    
    subgraph "Production Environment"
        PROD1[RabbitMQ Event Bus]
        PROD2[Distributed Services]
        PROD3[Persistent Queues]
        PROD4[Structured Logging]
        PROD5[Monitoring & Metrics]
        PROD6[Circuit Breakers]
        PROD7[Dead Letter Queues]
    end
    
    subgraph "Configuration"
        CONFIG[appsettings.json]
        ENV[Environment Variables]
    end
    
    CONFIG --> DEV1
    CONFIG --> PROD1
    ENV --> PROD1
    
    DEV1 --> DEV2
    DEV2 --> DEV3
    DEV3 --> DEV4
    
    PROD1 --> PROD2
    PROD2 --> PROD3
    PROD3 --> PROD4
    PROD4 --> PROD5
    PROD5 --> PROD6
    PROD6 --> PROD7
```