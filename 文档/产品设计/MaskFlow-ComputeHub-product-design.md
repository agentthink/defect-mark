# MaskFlow × ComputeHub 产品设计文档

> 版本：v1.0  
> 日期：2026-06-04  
> 产品形态：MaskFlow AI 自动标注平台 + ComputeHub 算力调度管理平台  
> 设计原则：前台无感使用，后台弹性调度；两套系统对接，但产品体验互不干扰。  

---

## 1. 文档目的

本文档用于定义 MaskFlow 与 ComputeHub 的完整产品设计方案，包括：

1. 产品定位与边界
2. 用户角色与使用场景
3. MaskFlow 用户端功能设计
4. ComputeHub 管理端功能设计
5. 两套系统的对接方式
6. 关键页面原型说明
7. 业务流程、任务流程与数据流
8. 套餐、额度与计费逻辑
9. API、数据模型与权限设计
10. 分阶段实施路线图

---

## 2. 产品总览

### 2.1 产品名称

| 产品 | 定位 | 面向对象 |
|---|---|---|
| MaskFlow | AI 图像分割与 YOLO 自动标注平台 | 标注用户、数据团队、模型训练用户 |
| ComputeHub | 算力调度、节点管理、任务执行与结算平台 | 平台管理员、算力提供者、运维人员 |

### 2.2 产品核心原则

MaskFlow 和 ComputeHub 是两套系统。

MaskFlow 面向普通标注用户，用户不需要理解算力平台、GPU 节点、资源池、Worker、Credits、Provider 等概念。

ComputeHub 面向平台管理者和算力提供者，用于处理节点、调度、任务、计费、风控和结算。

### 2.3 核心体验目标

普通用户看到的是：

```text
上传图片 → AI 自动分割 → 人工修正 → 保存标注 → 导出 YOLO 数据集
```

系统后台实际发生的是：

```text
MaskFlow 创建处理任务
        ↓
ComputeHub 创建 Job
        ↓
调度器分配 Worker / GPU 节点
        ↓
Worker 执行 SAM / YOLO / Export / Train
        ↓
结果回写 MaskFlow
        ↓
用户无感查看结果
```

---

## 3. 产品定位

### 3.1 MaskFlow 定位

MaskFlow 是一个面向公开服务演进的 AI 标注平台，核心能力包括：

- 图像上传
- AI 自动分割
- YOLO 检测框标注
- 标签管理
- 批量自动标注
- 数据集导出
- 文件管理
- 处理记录
- 账户与套餐管理

### 3.2 ComputeHub 定位

ComputeHub 是 MaskFlow 背后的算力调度基础设施，核心能力包括：

- 节点注册与管理
- GPU / CPU 资源池管理
- 任务队列调度
- Worker 执行管理
- 任务日志与重试
- 提供者管理
- 收益结算
- 计费规则
- 风控审计
- 平台运维监控

### 3.3 两套系统边界

| 维度 | MaskFlow | ComputeHub |
|---|---|---|
| 产品心智 | 标注工具 | 算力基础设施 |
| 用户对象 | 标注用户 | 管理员、Provider、运维 |
| 主要问题 | 标什么、怎么导出 | 跑在哪、怎么调度、花多少 |
| 页面语言 | AI 处理、自动标注、剩余次数 | Job、Node、Worker、Resource Pool |
| 是否显示 GPU | 不显示 | 显示 |
| 是否显示 Credits | 尽量不显示，对用户显示 AI 次数 | 显示 Credits / GPU 秒 / 结算金额 |
| 是否显示 Provider | 不显示 | 显示 |
| 是否显示资源池 | 不显示 | 显示 |

---

## 4. 当前技术背景

当前项目已经拆成三个独立部署单元：

```text
frontend/      Nginx 静态前端，页面与交互
api/           业务 API，用户、空间、上传、鉴权、SAM 转发
sam-service/   GPU 推理服务，PyTorch / Ultralytics / SAM3
sam3.pt        SAM3 模型文件，通过 volume 只挂载给 sam-service
```

当前访问入口：

```text
前端首页: http://127.0.0.1:3000/index.html
SAM 分割: http://127.0.0.1:3000/segment.html
YOLO 标注: http://127.0.0.1:3000/annotate.html
业务 API: http://127.0.0.1:8000
SAM 服务: 仅 Docker 内网访问，端口 8001
```

现阶段主要问题：

1. API 直连单一 `sam-service`
2. GPU 服务固定在 docker-compose 中
3. 用户请求容易同步阻塞
4. 不支持队列、优先级、失败重试
5. 不支持后续多卡、Provider、收益结算

目标演进方向：

```text
frontend → maskflow-api → task/job → compute-api/scheduler → worker → result
```

---

## 5. 用户角色

### 5.1 MaskFlow 用户角色

#### 普通用户

典型用户：

- AI 标注新手
- 小团队数据标注人员
- 个人开发者
- 课程 / 实验用户

核心诉求：

- 不想配置环境
- 不想关注 GPU
- 快速自动标注
- 导出 YOLO 数据集

#### 专业用户

典型用户：

- 算法工程师
- 数据集负责人
- 工业视觉工程师
- 模型训练人员

核心诉求：

- 批量自动标注
- 高质量标签管理
- 数据集版本控制
- 导出训练格式
- 可追溯的处理记录

#### 团队管理员

核心诉求：

- 管理团队成员
- 查看项目进度
- 控制用量
- 统一导出数据集
- 查看套餐和额度

---

### 5.2 ComputeHub 用户角色

#### 平台管理员

核心诉求：

- 查看全平台运行状态
- 管理节点
- 管理资源池
- 查看任务调度
- 配置计费规则
- 审核节点
- 处理异常

#### 算力提供者

核心诉求：

- 上架 GPU 节点
- 查看节点在线状态
- 查看收益
- 查看结算单
- 接收异常通知

#### 运维人员

核心诉求：

- 查看任务失败原因
- 查看节点健康状态
- 下线异常节点
- 查看审计日志
- 调整调度策略

---

## 6. 用户端无感设计原则

### 6.1 用户不应该看到的内容

MaskFlow 普通用户端不出现以下概念：

```text
算力共享
GPU 节点
Worker
Node Agent
资源池
Provider
Credits
GPU 秒
节点地区
社区节点
算力市场
```

### 6.2 用户应该看到的内容

MaskFlow 用户端只出现：

```text
AI 自动分割
批量自动标注
处理中
已完成
失败
可重试
今日剩余次数
套餐额度
处理记录
后台处理中
```

### 6.3 文案替换规则

| 后台真实概念 | MaskFlow 前台文案 |
|---|---|
| Job | 处理任务 |
| Queue | 等待处理 |
| Worker | AI 服务 |
| GPU 资源不足 | 当前处理繁忙 |
| Credits 不足 | AI 处理次数不足 |
| Job failed | 处理失败，请重试 |
| Scheduler retry | 系统正在自动重试 |
| Pool | 不展示 |
| Provider | 不展示 |
| Node offline | AI 服务暂时不可用 |

---

## 7. MaskFlow 产品结构

### 7.1 页面结构

```text
公开站点
├── 首页
├── 功能介绍
├── 价格
├── 登录
├── 注册

用户后台
├── 控制台
├── 项目管理
├── 上传图片
├── SAM 分割
├── YOLO 标注
├── 数据集导出
├── 文件管理
├── 处理记录
├── 账单套餐
└── 账户设置
```

### 7.2 核心业务闭环

```text
注册 / 登录
    ↓
创建项目
    ↓
上传图片
    ↓
AI 自动分割
    ↓
YOLO 标注修正
    ↓
保存标注
    ↓
导出数据集
    ↓
训练模型
```

---

## 8. MaskFlow 页面设计

## 8.1 首页

### 页面目标

让用户快速理解 MaskFlow 是一个简单、高效、无感加速的 AI 自动标注平台。

### 页面核心文案

主标题：

```text
AI 自动标注，让数据标注更简单高效
```

副标题：

```text
基于先进 AI 模型，自动分割目标，辅助人工校正，一键导出高质量数据集。
```

### 页面模块

1. 顶部导航
2. Hero 主视觉
3. 产品核心卖点
4. 使用流程
5. 功能卡片
6. 适用场景
7. 套餐入口
8. 页脚

### 主要卖点

```text
AI 自动分割
批量处理
标注工具
数据集导出
云端加速
安全可靠
```

### 注意

首页可以写“云端加速”，但不要写“算力共享平台”。

---

## 8.2 登录 / 注册页

### 页面目标

提供清晰的账号入口，支持用户快速进入工作台。

### 登录字段

```text
邮箱 / 用户名
密码
记住我
忘记密码
登录按钮
注册链接
```

### 注册字段

```text
邮箱
用户名
密码
同意用户协议
注册按钮
登录入口
```

### 交互要求

- 登录失败提示明确
- 注册成功自动进入控制台
- 表单校验前置
- 支持后续扩展第三方登录

---

## 8.3 控制台 Dashboard

### 页面目标

让用户看到项目、图片、标注、AI 处理次数和最近处理记录。

### 核心指标

```text
当前项目数
已上传图片
已完成标注
空间用量
今日 AI 处理次数
处理中任务
已完成任务
失败任务
```

### AI 额度卡片

展示：

```text
今日剩余：38 / 50 次
当前套餐：Free
升级套餐按钮
```

不展示：

```text
Credits
GPU 秒
Worker
节点
资源池
```

### 最近项目表

字段：

```text
项目名称
图片数量
标注数量
最近更新
操作
```

### 处理记录卡片

字段：

```text
处理名称
状态
图片数量
操作
```

---

## 8.4 项目管理

### 页面目标

管理用户的数据项目。

### 项目卡片字段

```text
项目封面
项目名称
项目描述
图片数量
标注数量
最近更新时间
进入标注
导出
更多操作
```

### 新建项目字段

```text
项目名称
项目描述
数据类型
默认数据集划分
存储位置
```

### 项目类型

```text
目标检测
实例分割
分类
混合项目
```

---

## 8.5 上传图片

### 页面目标

提供图片上传和文件夹上传能力。

### 功能

```text
拖拽上传
选择文件
选择文件夹
上传进度
失败重试
格式校验
大小校验
空间校验
```

### 支持格式

```text
jpg
jpeg
png
webp
bmp
```

### 上传限制

免费版建议：

```text
单文件最大 50MB
空间总量 1GB
单次最多 500 张
```

---

## 8.6 SAM 分割页

### 页面目标

让用户对单张图片进行 AI 分割，支持自动分割、点选分割、框选分割。

### 页面结构

```text
左侧：图片列表
中间：图片画布
右侧：AI 分割状态和分割模式
底部：状态栏
```

### 左侧图片列表

字段：

```text
缩略图
文件名
处理状态
上传更多
```

### 中间画布

功能：

```text
图片预览
mask 覆盖
缩放
拖动
撤销
重做
分割点提示
```

### 右侧 AI 状态卡片

显示：

```text
当前状态：正在分割
预计耗时：约 18 秒
进度：60%
图片数量：1 / 1 张
```

状态枚举：

```text
准备中
等待处理
正在分割
已完成
处理失败
已取消
```

### 分割模式

```text
自动分割
点选分割
框选分割
```

### 操作按钮

```text
开始分割
取消处理
清空结果
保存结果
发送到 YOLO 标注
```

### 无感要求

用户只看到“AI 分割状态”，不看到 Worker / GPU / 资源池。

---

## 8.7 YOLO 标注工作台

### 页面目标

完成图片的 YOLO 检测框标注，结合 AI 自动分割结果提升效率。

### 页面结构

```text
顶部导航 / 工具栏
左侧图片列表
中间标注画布
右侧标签管理 + AI 信息
底部状态栏
```

### 顶部工具栏

```text
搜索
批量自动标注
上一张
下一张
保存标注
导出当前 TXT
下载数据集 ZIP
```

### 左侧图片列表

字段：

```text
缩略图
文件名
处理状态
标注状态
```

### 中间画布

功能：

```text
显示原图
显示检测框
显示 mask
框选
移动
缩放
删除
撤销
重做
适配窗口
```

### 右侧标签管理

字段：

```text
标签名称
标签颜色
标签数量
添加标签
删除标签
修改标签
```

### AI 信息卡片

显示：

```text
AI 自动标注：已完成
置信度：0.95
处理时间：12.4s
模型版本：MaskFlow-YOLOv8
重新运行 AI
```

不显示：

```text
GPU
Worker
Node
Credits
资源池
```

### 底部状态栏

```text
AI 辅助：正常
当前图片
标注状态
分辨率
标注数量
文件大小
```

---

## 8.8 处理记录

### 页面目标

让用户查看所有 AI 处理任务的业务状态。

### 页面命名

用户端叫：

```text
处理记录
```

不要叫：

```text
任务调度
算力任务
Compute Job
```

### 筛选 Tab

```text
全部
运行中
已完成
失败
已取消
```

### 表格字段

```text
任务名称
处理类型
所属项目
图片数量
状态
开始时间
操作
```

### 处理类型

```text
自动分割
批量自动标注
数据集导出
模型训练
```

### 状态

```text
等待中
运行中
已完成
失败
已取消
```

### 操作

```text
查看
取消
重试
删除记录
```

---

## 8.9 数据集导出

### 页面目标

将项目中的图片和标注导出为标准 YOLO 数据集。

### 导出配置

```text
选择项目
训练集比例
验证集比例
测试集比例
标注格式
包含图片
包含标签
包含配置文件
文件命名方式
```

### 默认比例

```text
train: 70%
val: 20%
test: 10%
```

### 导出格式

```text
YOLO txt
COCO JSON
Pascal VOC XML
```

MVP 优先支持 YOLO txt。

### ZIP 结构

```text
dataset/
  images/
    train/
    val/
    test/
  labels/
    train/
    val/
    test/
  dataset.yaml
  classes.txt
  README.md
```

---

## 8.10 文件管理

### 页面目标

管理用户上传和生成的文件。

### 文件分类

```text
全部文件
图片
标注文件
导出文件
```

### 表格字段

```text
文件名
类型
大小
所属项目
上传时间
操作
```

### 操作

```text
查看
下载
删除
移动
重命名
```

---

## 8.11 账单套餐

### 页面目标

让用户理解套餐权益，并升级获得更多 AI 处理次数和存储空间。

### 套餐设计

#### Free

```text
¥0 / 月
10G 存储空间
每日 50 次 AI 处理
基础标注工具
数据集导出
```

#### Pro

```text
¥49 / 月
50G 存储空间
每日 1000 次 AI 处理
高级标注工具
团队协作功能
更多数据集导出
```

#### Team

```text
¥299 / 月
500G 存储空间
无限次 AI 处理
高级标注工具
团队协作
专属技术支持
自定义导出
```

### 注意

用户端显示“AI 处理次数”，不显示 GPU 秒和算力点。

---

## 8.12 账户设置

### 页面结构

```text
个人信息
修改密码
通知设置
API Token
团队管理
设备管理
```

### 个人信息字段

```text
头像
用户名
邮箱
手机号
保存修改
```

### API Token

用于高级用户通过接口上传、创建处理任务和导出数据集。

---

## 9. ComputeHub 产品结构

### 9.1 页面结构

```text
ComputeHub 管理端
├── 平台控制台
├── 节点管理
├── 任务调度
├── 资源池管理
├── 提供者管理
├── 收益结算
├── 计费规则
├── 风控审计
└── 系统设置
```

### 9.2 设计定位

ComputeHub 是后台系统，可以展示专业技术字段：

```text
GPU 型号
显存
节点 ID
Worker
资源池
Job ID
Credits
GPU 秒
Provider
调度策略
心跳
信誉分
收益结算
```

---

## 10. ComputeHub 页面设计

## 10.1 平台控制台

### 页面目标

实时监控全平台算力运行状态。

### 指标卡片

```text
在线节点数
今日任务数
队列深度
平均等待时间
成功率
平台收入
```

### 图表

```text
任务趋势
资源池占用
收入趋势
失败率趋势
```

### 最近任务表

字段：

```text
Job ID
来源应用
类型
节点
状态
耗时
费用
```

### 异常告警

字段：

```text
告警类型
节点
等级
发生时间
状态
操作
```

---

## 10.2 节点管理

### 页面目标

管理所有算力节点，查看节点健康、性能与审核状态。

### 指标

```text
在线节点
离线节点
GPU 节点
待审核节点
异常节点
```

### 表格字段

```text
节点 ID
提供者
资源池
GPU 型号
显存
地区
状态
价格
信誉分
最后心跳
操作
```

### 详情面板

```text
硬件信息
驱动环境
资源利用率
运行信息
事件日志
```

### 操作

```text
审核通过
下线节点
查看日志
封禁节点
修改资源池
```

---

## 10.3 任务调度

### 页面目标

管理 Job 队列、状态和执行日志。

### 指标

```text
队列中
运行中
今日完成
今日失败
平均等待时长
平均执行时长
```

### 表格字段

```text
Job ID
来源应用
用户 ID
任务类型
资源池
指派节点
状态
重试次数
排队时长
执行时长
费用
创建时间
操作
```

### 任务状态

```text
pending
queued
running
succeeded
failed
cancelled
```

### 右侧详情

```text
基本信息
执行进度
资源分配
执行日志
输入输出
计费信息
错误信息
```

### 操作

```text
取消任务
重试任务
查看输出
查看日志
```

---

## 10.4 资源池管理

### 页面目标

管理不同类型资源池的容量、调度策略和使用率。

### 资源池类型

```text
platform-gpu
community-gpu
dedicated-team
cpu-general
reserved-cn-east
```

### 指标

```text
资源池总数
总容量
已分配
利用率
预留容量
异常池
```

### 表格字段

```text
资源池名称
类型
地区
节点数
总容量
已用容量
利用率
调度策略
状态
操作
```

### 详情面板

```text
容量概览
近 7 天利用率
分配与调度策略
自动扩缩容
预留与保障
```

---

## 10.5 提供者管理

### 页面目标

管理算力提供者的入驻、节点、收益和结算状态。

### 指标

```text
提供者总数
活跃提供者
待审核提供者
总节点数
本月结算金额
平均在线率
```

### 表格字段

```text
提供者
邮箱
地区
节点数
在线率
收入分成
待结算
状态
注册时间
操作
```

### 详情面板

```text
提供者信息
本月收入
可提现金额
信誉分
收入趋势
节点清单摘要
```

### 操作

```text
审核
查看节点
发起结算
冻结账户
```

---

## 10.6 收益结算

### 页面目标

管理平台与提供者之间的收益结算。

### 指标

```text
待结算金额
本月已结算
待打款提供者
异常结算单
平台抽成收入
平均结算周期
```

### Tab

```text
待结算
已结算
异常单
提现申请
```

### 表格字段

```text
结算单号
提供者
周期
节点数
应结算金额
平台抽成
实付金额
状态
创建时间
操作
```

### 详情面板

```text
结算周期
结算概览
最近收入趋势
费用明细
发票与税务
打款账户
```

### 操作

```text
审核结算单
确认打款
查看明细
驳回申请
```

---

## 10.7 计费规则

### 页面目标

配置平台资源价格、套餐策略、折扣规则和抽成比例。

### 指标

```text
生效规则数
待发布规则
价格异常提醒
GPU 基础单价
平台抽成比例
预计月收入
```

### Tab

```text
资源定价
套餐策略
峰谷时段
优惠与折扣
变更历史
```

### 表格字段

```text
规则名称
资源类型
适用资源池
地区
基础单价
计费方式
状态
生效时间
更新时间
操作
```

### 配置面板

```text
GPU 型号分档
基础定价
峰时倍率
谷时倍率
平台抽成比例
最小计费单位
超时策略
有效期
```

---

## 10.8 风控审计

### 页面目标

监控安全风险、异常节点和合规事件。

### 指标

```text
今日告警
紧急事件
风险节点
审核待处理
合规通过率
```

### 异常类型

```text
节点离线
GPU 利用率异常
任务失败异常
可疑流量
地域不合规
GPU 利用率突增
```

### 告警表字段

```text
告警类型
对象
严重级别
触发时间
当前状态
处理人
操作
```

### 待审核节点

字段：

```text
节点 ID
提供者
GPU 型号
地区
资料完整度
审核操作
```

### 审计日志

```text
时间
事件
对象
结果
操作人
备注
```

---

## 11. 系统对接设计

### 11.1 对接原则

1. MaskFlow 前端不直连 ComputeHub
2. MaskFlow 用户不直接感知 ComputeHub
3. MaskFlow API 负责创建业务任务
4. ComputeHub 负责创建和调度 Job
5. Worker 完成后将结果写回对象存储或业务 API
6. MaskFlow 只展示业务状态

### 11.2 系统链路

```text
MaskFlow Frontend
    ↓
MaskFlow API
    ↓
Task Service
    ↓
ComputeHub API
    ↓
Scheduler
    ↓
Worker / Node Agent
    ↓
Object Storage
    ↓
MaskFlow API
    ↓
MaskFlow Frontend
```

### 11.3 业务任务与计算 Job 映射

| MaskFlow Task | ComputeHub Job |
|---|---|
| 自动分割 | sam.segment |
| 自动多 mask | sam.masks |
| 批量自动标注 | sam.batch |
| 数据集导出 | dataset.export |
| 云端训练 | yolo.train |

---

## 12. Job 模型

### 12.1 Job 状态机

```text
pending → queued → running → succeeded
                    ↘ failed
                    ↘ cancelled
```

### 12.2 Job 字段

```json
{
  "id": "job_01HXXX",
  "app": "maskflow",
  "type": "sam.segment",
  "user_id": 12,
  "project_id": "proj_abc",
  "priority": "free",
  "pool": "platform",
  "status": "queued",
  "resources": {
    "gpu": 1,
    "vram_gb_min": 8,
    "cpu": 2,
    "memory_gb": 16,
    "timeout_sec": 600
  },
  "input": {
    "image_uri": "file:///data/storage/12/xxx.jpg"
  },
  "params": {
    "conf": 0.25,
    "half": true,
    "mode": "auto"
  },
  "output": {
    "prefix_uri": "file:///data/storage/12/outputs/job_01HXXX/"
  },
  "billing": {
    "reserved_credits": 10,
    "charged_credits": null
  },
  "node_id": null,
  "created_at": "2026-06-04T10:00:00Z",
  "started_at": null,
  "finished_at": null,
  "error": null
}
```

---

## 13. MaskFlow Task 模型

### 13.1 Task 字段

```json
{
  "id": "task_01HXXX",
  "jobId": "job_01HXXX",
  "type": "auto_segment",
  "title": "自动分割_image_001",
  "userId": 12,
  "projectId": "proj_abc",
  "fileId": 42,
  "imageCount": 1,
  "status": "running",
  "progress": 0.6,
  "startedAt": "2026-06-04T10:00:05Z",
  "finishedAt": null,
  "result": null,
  "errorMessage": null
}
```

### 13.2 Task 状态映射

| ComputeHub Job 状态 | MaskFlow 用户端状态 |
|---|---|
| pending | 等待处理 |
| queued | 等待处理 |
| running | 处理中 |
| succeeded | 已完成 |
| failed | 失败 |
| cancelled | 已取消 |

---

## 14. 套餐与额度设计

### 14.1 用户端套餐展示

| 套餐 | 存储 | AI 处理次数 | 适合用户 |
|---|---:|---:|---|
| Free | 10G | 50 次 / 日 | 个人体验 |
| Pro | 50G | 1000 次 / 日 | 专业用户 |
| Team | 500G | 无限 / 高额度 | 团队协作 |

### 14.2 后台额度映射

| 套餐 | 用户看到 | 后台映射 |
|---|---|---|
| Free | 每日 50 次 AI 处理 | 低优先级 + 每日点数 |
| Pro | 每日 1000 次 AI 处理 | 中高优先级 + 更多点数 |
| Team | 无限 / 专属支持 | 高优先级 + 专属队列 |

### 14.3 处理次数消耗建议

| 操作 | 用户端消耗 | 后台 Credits |
|---|---:|---:|
| 单图自动分割 | 1 次 | 10 |
| 自动多 mask | 1 次 | 15 |
| 批量自动标注 | 按图片数 | 每张 8 |
| 数据集导出 | 不消耗或少量 | 1-5 |
| 云端训练 | 高级套餐功能 | 按 GPU 秒 |

---

## 15. API 设计

## 15.1 MaskFlow API

### 用户认证

```text
POST /api/auth/register
POST /api/auth/login
GET  /api/me
```

### 文件上传

```text
POST /api/files/upload
GET  /api/files
DELETE /api/files/{id}
```

### 项目

```text
POST /api/projects
GET  /api/projects
GET  /api/projects/{id}
PUT  /api/projects/{id}
DELETE /api/projects/{id}
```

### 处理任务

```text
POST /api/tasks
GET  /api/tasks
GET  /api/tasks/{id}
POST /api/tasks/{id}/cancel
POST /api/tasks/{id}/retry
```

### AI 额度

```text
GET /api/ai/quota
```

### 数据集导出

```text
POST /api/export/dataset
GET  /api/export/{id}
```

### 兼容接口

```text
POST /api/segment
POST /api/annotation/masks
```

---

## 15.2 ComputeHub API

### 节点

```text
POST /v1/nodes/register
POST /v1/nodes/{id}/heartbeat
GET  /v1/nodes
GET  /v1/nodes/{id}
POST /v1/nodes/{id}/offline
POST /v1/nodes/{id}/approve
```

### Job

```text
POST /v1/jobs
GET  /v1/jobs
GET  /v1/jobs/{id}
POST /v1/jobs/{id}/cancel
POST /v1/jobs/{id}/retry
POST /v1/jobs/{id}/events
```

### Worker 拉取

```text
GET /v1/nodes/{id}/jobs/poll
```

### 资源池

```text
GET  /v1/pools
POST /v1/pools
PUT  /v1/pools/{id}
```

### 计费

```text
GET  /v1/pricing
POST /v1/pricing/rules
GET  /v1/wallet/balance
GET  /v1/settlements
```

---

## 16. 数据库设计

### 16.1 MaskFlow 增量表

```sql
CREATE TABLE tasks (
  id TEXT PRIMARY KEY,
  user_id INTEGER NOT NULL,
  job_id TEXT NOT NULL UNIQUE,
  type TEXT NOT NULL,
  title TEXT,
  project_id TEXT,
  file_id INTEGER,
  image_count INTEGER DEFAULT 1,
  status TEXT NOT NULL,
  progress REAL DEFAULT 0,
  result_json TEXT,
  error_message TEXT,
  created_at TEXT NOT NULL,
  started_at TEXT,
  finished_at TEXT
);

CREATE TABLE user_ai_quota (
  user_id INTEGER PRIMARY KEY,
  plan TEXT NOT NULL DEFAULT 'free',
  daily_limit INTEGER NOT NULL DEFAULT 50,
  daily_used INTEGER NOT NULL DEFAULT 0,
  daily_reset_at TEXT NOT NULL
);
```

### 16.2 ComputeHub 表

```sql
CREATE TABLE nodes (
  id TEXT PRIMARY KEY,
  owner_id INTEGER NOT NULL,
  pool TEXT NOT NULL DEFAULT 'community',
  status TEXT NOT NULL,
  gpu_model TEXT,
  vram_gb INTEGER,
  region TEXT,
  price_per_hour REAL,
  reputation REAL DEFAULT 1.0,
  last_heartbeat TEXT
);

CREATE TABLE jobs (
  id TEXT PRIMARY KEY,
  app TEXT NOT NULL,
  type TEXT NOT NULL,
  user_id INTEGER NOT NULL,
  project_id TEXT,
  pool TEXT NOT NULL,
  priority TEXT NOT NULL,
  status TEXT NOT NULL,
  resources_json TEXT NOT NULL,
  input_json TEXT NOT NULL,
  output_json TEXT,
  params_json TEXT,
  node_id TEXT,
  reserved_credits INTEGER,
  charged_credits INTEGER,
  error TEXT,
  created_at TEXT NOT NULL,
  started_at TEXT,
  finished_at TEXT
);

CREATE TABLE job_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  job_id TEXT NOT NULL,
  event_type TEXT NOT NULL,
  payload_json TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE allocations (
  job_id TEXT PRIMARY KEY,
  node_id TEXT NOT NULL,
  gpu_index INTEGER NOT NULL,
  started_at TEXT NOT NULL,
  ended_at TEXT
);

CREATE TABLE wallet_ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id INTEGER NOT NULL,
  delta INTEGER NOT NULL,
  reason TEXT NOT NULL,
  job_id TEXT,
  created_at TEXT NOT NULL
);
```

---

## 17. 权限设计

### 17.1 MaskFlow 权限

| 权限 | Free | Pro | Team |
|---|---|---|---|
| 创建项目 | 有限制 | 更多 | 无限 / 高额度 |
| 上传图片 | 有限制 | 更多 | 团队空间 |
| AI 自动处理 | 50 次 / 日 | 1000 次 / 日 | 高额度 |
| 批量标注 | 限制 | 支持 | 支持 |
| 数据集导出 | 支持 | 支持 | 支持 |
| 团队协作 | 不支持 | 部分支持 | 支持 |
| API Token | 限制 | 支持 | 支持 |

### 17.2 ComputeHub 权限

| 角色 | 权限 |
|---|---|
| Admin | 全部权限 |
| Operator | 节点、任务、告警处理 |
| Finance | 收益、结算、计费 |
| Provider | 自己的节点和收益 |
| Auditor | 只读审计 |

---

## 18. 异常与提示设计

### 18.1 MaskFlow 用户端提示

| 场景 | 提示 |
|---|---|
| AI 处理中 | AI 正在处理，请稍候 |
| 队列繁忙 | 当前处理人数较多，请稍候 |
| 处理失败 | 处理失败，可点击重试 |
| 次数不足 | 今日 AI 处理次数已用完 |
| 文件过大 | 文件超过上传限制 |
| 空间不足 | 存储空间不足，请清理或升级 |
| 服务不可用 | AI 服务暂时不可用，请稍后重试 |

### 18.2 ComputeHub 管理端错误码

| HTTP | code | 说明 |
|---|---|---|
| 402 | QUOTA_EXCEEDED | 额度不足 |
| 429 | DAILY_SEGMENT_LIMIT | 每日处理次数用尽 |
| 503 | NO_WORKER_AVAILABLE | 无可用 Worker |
| 504 | JOB_TIMEOUT | Job 超时 |
| 500 | WORKER_ERROR | Worker 执行失败 |

---

## 19. 安全设计

### 19.1 数据安全

- 用户上传文件必须做类型校验
- 上传文件大小限制
- 存储空间配额校验
- 导出文件限时下载
- 私有文件必须鉴权访问
- 后续对象存储使用 signed URL

### 19.2 算力安全

- Worker 使用平台签名镜像
- 禁止普通用户上传自定义执行代码
- Job 设置超时时间
- 节点心跳检测
- 异常节点自动下线
- 失败任务支持换节点重试
- Provider 节点不持久化用户数据

### 19.3 认证安全

- 用户访问 MaskFlow 使用 Bearer Token / Session
- MaskFlow 到 ComputeHub 使用服务间 HMAC 或内部 JWT
- Node Agent 到 ComputeHub 使用 Node API Key + mTLS
- 管理端操作需要审计日志

---

## 20. 日志与审计

### 20.1 MaskFlow 日志

```text
用户登录
文件上传
项目创建
AI 处理创建
标注保存
数据集导出
套餐变更
```

### 20.2 ComputeHub 日志

```text
节点注册
节点心跳
任务创建
任务调度
任务取消
任务失败
任务重试
计费扣减
结算发起
管理员操作
```

---

## 21. 分阶段路线图

### Phase 0：重构兼容层

目标：

```text
封装 create_segment_job()
逻辑仍直连单 sam-service
前端无变化
```

交付：

```text
compute 模块雏形
task 创建能力
兼容现有接口
```

### Phase 1：自营多 Worker + 队列

目标：

```text
支持多个 sam-service 副本
支持任务队列
支持处理记录
支持 AI 次数限制
不开放 Provider
```

交付：

```text
tasks 表
jobs 表
user_ai_quota 表
SAM_WORKERS 环境变量
/api/tasks
/api/ai/quota
处理记录页面
```

### Phase 2：ComputeHub 独立服务

目标：

```text
compute-api 独立
异步任务为主
WebSocket / 轮询进度
引入 MinIO
```

交付：

```text
compute-api/
资源池管理
任务调度管理
节点管理
WebSocket 进度
```

### Phase 3：开放 Provider

目标：

```text
Provider 节点上架
Node Agent
Community Pool
收益分成
结算系统
```

交付：

```text
Provider 控制台
节点审核
收益结算
风控审计
```

### Phase 4：训练闭环

目标：

```text
支持 yolo.train
云端训练
训练结果管理
模型版本管理
```

交付：

```text
train-worker
训练任务
模型管理
训练日志
```

---

## 22. MVP 范围

### 22.1 MaskFlow MVP

必须做：

```text
首页
登录 / 注册
控制台
项目管理
上传图片
SAM 分割
YOLO 标注
数据集导出
文件管理
处理记录
账单套餐
账户设置
```

### 22.2 ComputeHub MVP

Phase 1 可以不独立给用户开放完整 ComputeHub，但管理端建议先做：

```text
平台控制台
节点管理
任务调度
资源池管理
风控告警
```

收益结算、Provider 管理、计费规则可以放到 Phase 3。

---

## 23. 原型页面清单

### 23.1 MaskFlow 用户端原型

1. 首页
2. 登录 / 注册
3. 控制台
4. SAM 分割页
5. YOLO 标注工作台
6. 处理记录
7. 数据集导出
8. 文件管理
9. 账单套餐
10. 账户设置

### 23.2 ComputeHub 管理端原型

1. 平台控制台
2. 节点管理
3. 任务调度
4. 资源池管理
5. 提供者管理
6. 风控审计
7. 收益结算
8. 计费规则

---

## 24. 关键产品决策

| 问题 | 决策 |
|---|---|
| 是否让 MaskFlow 用户选择算力来源 | 否 |
| 是否在 MaskFlow 显示 GPU / Worker | 否 |
| 是否做两套系统 | 是 |
| MaskFlow 是否无感使用 ComputeHub | 是 |
| ComputeHub 是否独立品牌 | 是 |
| MVP 是否开放个人上架 GPU | 否 |
| 用户端展示次数还是 Credits | 展示次数 |
| 后台是否使用 Credits | 是 |
| Phase 1 是否保留同步接口 | 是 |
| Phase 2 是否转异步任务 | 是 |

---

## 25. 成功指标

### 25.1 MaskFlow 指标

```text
注册转化率
首张图片上传率
首次 AI 分割完成率
标注保存率
数据集导出率
AI 处理成功率
平均处理等待时间
Pro 转化率
```

### 25.2 ComputeHub 指标

```text
在线节点数
任务成功率
平均排队时长
平均执行时长
节点利用率
失败重试率
平台收入
Provider 在线率
结算准确率
异常告警处理时长
```

---

## 26. 总结

MaskFlow 与 ComputeHub 的最终产品形态应当是：

```text
MaskFlow：面向标注用户，简单、高效、无感使用 AI 能力
ComputeHub：面向平台和提供者，负责节点、调度、计费、风控、结算
```

普通用户在 MaskFlow 中只看到“AI 自动处理”和“处理记录”，不需要理解背后的算力来源。

系统通过统一 Task / Job 模型完成对接：

```text
MaskFlow Task = 用户可见的业务处理记录
ComputeHub Job = 后台可调度、可计费、可审计的执行单元
```

这种设计既保证了 MaskFlow 的简单易用，也为后续多卡、自营资源池、共享算力市场、Provider 分成和云端训练闭环打下基础。
