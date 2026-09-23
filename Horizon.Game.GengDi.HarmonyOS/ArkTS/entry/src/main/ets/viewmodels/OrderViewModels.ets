import { AppContext } from "../model/AppContext";
import { MarketItem, MarketOrder } from "./FlowerViewModels";
import { ApiClient, ServerOrderItemRequest } from "../services/ApiClient";

/** 收货地址 */
export class ShippingAddress {
  addressId: string = '';
  /** 收件人姓名 */
  receiverName: string = '';
  /** 联系电话 */
  phone: string = '';
  /** 省份 */
  province: string = '';
  /** 城市 */
  city: string = '';
  /** 详细地址 */
  detail: string = '';
  /** 是否默认地址 */
  isDefault: boolean = false;
  /** 创建时间 */
  createTime: string = '';

  /** 完整地址文本 */
  get fullAddress(): string {
    return `${this.province} ${this.city} ${this.detail}`;
  }
}

/** 支付方式 */
export enum PaymentMethod {
  WECHAT = 'wechat',
  ALIPAY = 'alipay'
}

/** 订单状态 */
export enum OrderStatus {
  PENDING_PAYMENT = 'pending_payment',      // 待支付
  PAID = 'paid',                             // 已支付
  PROCESSING = 'processing',                 // 处理中
  SHIPPED = 'shipped',                       // 已发货
  COMPLETED = 'completed',                   // 已完成
  CANCELLED = 'cancelled'                     // 已取消
}

/** 订单商品项 */
export class OrderItem {
  speciesId: number = 0;
  name: string = '';
  emoji: string = '🌸';
  /** 单价（元/扎）*/
  unitPrice: number = 0;
  /** 数量（扎）*/
  quantity: number = 0;
  /** 小计金额 */
  subtotal: number = 0;
}

/** 订单主记录 */
export class FlowerOrder {
  orderId: string = '';
  /** 订单编号（格式化）*/
  orderNo: string = '';
  /** 订单状态 */
  status: OrderStatus = OrderStatus.PENDING_PAYMENT;
  /** 订单状态文本 */
  statusText: string = '待支付';
  /** 订单商品列表 */
  items: OrderItem[] = [];
  /** 收货地址快照 */
  addressSnapshot: ShippingAddress | null = null;
  /** 支付方式 */
  paymentMethod: PaymentMethod = PaymentMethod.WECHAT;
  /** 商品总额 */
  goodsTotal: number = 0;
  /** 运费 */
  shippingFee: number = 0;
  /** 订单总额 */
  totalAmount: number = 0;
  /** 优惠金额 */
  discount: number = 0;
  /** 实际支付金额 */
  payAmount: number = 0;
  /** 下单时间 */
  createTime: string = '';
  /** 支付时间 */
  payTime: string = '';
  /** 发货时间 */
  shipTime: string = '';
  /** 完成时间 */
  completeTime: string = '';
  /** 物流单号 */
  trackingNo: string = '';
  /** 物流公司 */
  trackingCompany: string = '';
  /** 备注 */
  remark: string = '';
  /** 服务端订单ID（0=未同步服务端，仅本地） */
  serverOrderId: number = 0;
  /** 服务端订单号 */
  serverOrderNo: string = '';
  /** 服务端核算实付金额（以服务端商品价格为准） */
  serverPayAmount: number = 0;
}

/** 物流信息 */
export class ShipmentInfo {
  trackingNo: string = '';
  company: string = '';
  status: string = '';
  updateTime: string = '';
  /** 物流轨迹 */
  tracks: ShipmentTrack[] = [];
}

/** 物流轨迹节点 */
export class ShipmentTrack {
  time: string = '';
  description: string = '';
}

/** 地址持久化键 */
const KEY_SHIPPING_ADDRESSES = 'shippingAddresses';
/** 订单持久化键 */
const KEY_FLOWER_ORDERS = 'flowerOrders';
/** 购物车持久化键 */
const KEY_CART_ITEMS = 'cartItems';

/** 地址管理ViewModel */
@Observed
export class AddressViewModel {
  addresses: ShippingAddress[] = [];
  loading: boolean = false;

  async loadAddresses(): Promise<void> {
    this.loading = true;
    try {
      const raw = await AppContext.getInstance().getPrefString(KEY_SHIPPING_ADDRESSES, '[]');
      const arr = JSON.parse(raw) as object[];
      const list: ShippingAddress[] = [];
      if (Array.isArray(arr)) {
        for (const item of arr) {
          const addr = new ShippingAddress();
          const r = item as Record<string, Object>;
          addr.addressId = String(r['addressId'] ?? '');
          addr.receiverName = String(r['receiverName'] ?? '');
          addr.phone = String(r['phone'] ?? '');
          addr.province = String(r['province'] ?? '');
          addr.city = String(r['city'] ?? '');
          addr.detail = String(r['detail'] ?? '');
          addr.isDefault = Boolean(r['isDefault'] ?? false);
          addr.createTime = String(r['createTime'] ?? '');
          list.push(addr);
        }
      }
      this.addresses = list;
    }
    finally {
      this.loading = false;
    }
  }

  /** 获取默认地址 */
  getDefaultAddress(): ShippingAddress | null {
    for (const addr of this.addresses) {
      if (addr.isDefault) {
        return addr;
      }
    }
    return this.addresses.length > 0 ? this.addresses[0] : null;
  }

  /** 保存地址 */
  async saveAddress(address: ShippingAddress): Promise<void> {
    if (!address.addressId) {
      address.addressId = `addr_${Date.now()}`;
      address.createTime = new Date().toISOString();
      if (this.addresses.length === 0) {
        address.isDefault = true;
      }
    }
    // 如果设为默认，取消其他地址的默认
    if (address.isDefault) {
      for (const addr of this.addresses) {
        addr.isDefault = false;
      }
    }
    const idx = this.addresses.findIndex((a: ShippingAddress) => a.addressId === address.addressId);
    if (idx >= 0) {
      this.addresses[idx] = address;
    }
    else {
      this.addresses = [address, ...this.addresses];
    }
    await this.saveToStorage();
  }

  /** 删除地址 */
  async deleteAddress(addressId: string): Promise<void> {
    this.addresses = this.addresses.filter((a: ShippingAddress) => a.addressId !== addressId);
    await this.saveToStorage();
  }

  private async saveToStorage(): Promise<void> {
    await AppContext.getInstance().putPrefString(KEY_SHIPPING_ADDRESSES, JSON.stringify(this.addresses));
  }
}

/** 订单管理ViewModel */
@Observed
export class OrderViewModel {
  orders: FlowerOrder[] = [];
  loading: boolean = false;

  async loadOrders(): Promise<void> {
    this.loading = true;
    try {
      const raw = await AppContext.getInstance().getPrefString(KEY_FLOWER_ORDERS, '[]');
      const arr = JSON.parse(raw) as object[];
      const list: FlowerOrder[] = [];
      if (Array.isArray(arr)) {
        for (const item of arr) {
          list.push(this.parseOrder(item as Record<string, Object>));
        }
      }
      this.orders = list;
    }
    finally {
      this.loading = false;
    }
  }

  /** 创建订单 */
  createOrder(items: OrderItem[], address: ShippingAddress, payment: PaymentMethod, remark: string): FlowerOrder {
    const order = new FlowerOrder();
    order.orderId = `order_${Date.now()}_${Math.floor(Math.random() * 10000)}`;
    order.orderNo = `FH${Date.now()}`;
    order.status = OrderStatus.PENDING_PAYMENT;
    order.statusText = '待支付';
    order.items = items;
    order.addressSnapshot = address;
    order.paymentMethod = payment;
    order.remark = remark;
    order.createTime = new Date().toISOString();

    // 计算金额
    let goodsTotal = 0;
    for (const item of items) {
      item.subtotal = Math.round(item.unitPrice * item.quantity * 100) / 100;
      goodsTotal += item.subtotal;
    }
    order.goodsTotal = Math.round(goodsTotal * 100) / 100;
    // 运费：满100免运费
    order.shippingFee = order.goodsTotal >= 100 ? 0 : 8;
    order.discount = 0;
    order.totalAmount = Math.round((order.goodsTotal + order.shippingFee) * 100) / 100;
    order.payAmount = order.totalAmount;

    return order;
  }

  /** 保存订单 */
  async saveOrder(order: FlowerOrder): Promise<void> {
    const idx = this.orders.findIndex((o: FlowerOrder) => o.orderId === order.orderId);
    if (idx >= 0) {
      this.orders[idx] = order;
    }
    else {
      this.orders = [order, ...this.orders];
    }
    await this.saveToStorage();
  }

  /**
   * 订单同步服务端（正式支付前置）：品种直购下单，金额以服务端核算为准。
   * 成功后回填 serverOrderId/serverOrderNo/serverPayAmount 并持久化。
   */
  async submitToServerAsync(order: FlowerOrder): Promise<boolean> {
    try {
      if (!order || order.items.length === 0) {
        return false;
      }
      const items: ServerOrderItemRequest[] = [];
      for (const item of order.items) {
        const req = new ServerOrderItemRequest();
        req.speciesId = item.speciesId;
        req.productName = item.name;
        req.quantity = item.quantity;
        items.push(req);
      }
      const address = order.addressSnapshot;
      const shipTo = address ? address.receiverName : '';
      const cellPhone = address ? address.phone : '';
      const addressText = address ? address.fullAddress : '';
      const result = await ApiClient.getInstance().createServerOrder(items, shipTo, cellPhone, addressText);
      if (result.orderId <= 0) {
        return false;
      }
      order.serverOrderId = result.orderId;
      order.serverOrderNo = result.orderNo;
      order.serverPayAmount = result.orderTotalAmount;
      await this.saveOrder(order);
      return true;
    }
    catch (e) {
      return false;
    }
  }

  /** 取消订单 */
  async cancelOrder(orderId: string): Promise<void> {
    const order = this.orders.find((o: FlowerOrder) => o.orderId === orderId);
    if (order) {
      order.status = OrderStatus.CANCELLED;
      order.statusText = '已取消';
      await this.saveOrder(order);
    }
  }

  /** 支付订单（模拟）*/
  async payOrder(orderId: string): Promise<void> {
    const order = this.orders.find((o: FlowerOrder) => o.orderId === orderId);
    if (order) {
      order.status = OrderStatus.PAID;
      order.statusText = '已支付';
      order.payTime = new Date().toISOString();
      await this.saveOrder(order);
    }
  }

  /** 模拟发货 */
  async shipOrder(orderId: string): Promise<void> {
    const order = this.orders.find((o: FlowerOrder) => o.orderId === orderId);
    if (order) {
      order.status = OrderStatus.SHIPPED;
      order.statusText = '已发货';
      order.shipTime = new Date().toISOString();
      order.trackingNo = `SF${Date.now()}`;
      order.trackingCompany = '顺丰速运';
      await this.saveOrder(order);
    }
  }

  /** 确认收货 */
  async confirmReceive(orderId: string): Promise<void> {
    const order = this.orders.find((o: FlowerOrder) => o.orderId === orderId);
    if (order) {
      order.status = OrderStatus.COMPLETED;
      order.statusText = '已完成';
      order.completeTime = new Date().toISOString();
      await this.saveOrder(order);
    }
  }

  /** 获取状态对应的订单列表 */
  getOrdersByStatus(status: OrderStatus | null): FlowerOrder[] {
    if (status === null) {
      return this.orders;
    }
    return this.orders.filter((o: FlowerOrder) => o.status === status);
  }

  private parseOrder(r: Record<string, Object>): FlowerOrder {
    const order = new FlowerOrder();
    order.orderId = String(r['orderId'] ?? '');
    order.orderNo = String(r['orderNo'] ?? '');
    order.status = (r['status'] as OrderStatus) ?? OrderStatus.PENDING_PAYMENT;
    order.statusText = String(r['statusText'] ?? '');
    order.paymentMethod = (r['paymentMethod'] as PaymentMethod) ?? PaymentMethod.WECHAT;
    order.goodsTotal = Number(r['goodsTotal'] ?? 0);
    order.shippingFee = Number(r['shippingFee'] ?? 0);
    order.totalAmount = Number(r['totalAmount'] ?? 0);
    order.payAmount = Number(r['payAmount'] ?? 0);
    order.discount = Number(r['discount'] ?? 0);
    order.createTime = String(r['createTime'] ?? '');
    order.payTime = String(r['payTime'] ?? '');
    order.shipTime = String(r['shipTime'] ?? '');
    order.completeTime = String(r['completeTime'] ?? '');
    order.trackingNo = String(r['trackingNo'] ?? '');
    order.trackingCompany = String(r['trackingCompany'] ?? '');
    order.remark = String(r['remark'] ?? '');
    order.serverOrderId = Number(r['serverOrderId'] ?? 0);
    order.serverOrderNo = String(r['serverOrderNo'] ?? '');
    order.serverPayAmount = Number(r['serverPayAmount'] ?? 0);

    // 解析商品项
    const itemsArr = r['items'] as object[];
    if (Array.isArray(itemsArr)) {
      for (const item of itemsArr) {
        const oi = new OrderItem();
        const ir = item as Record<string, Object>;
        oi.speciesId = Number(ir['speciesId'] ?? 0);
        oi.name = String(ir['name'] ?? '');
        oi.emoji = String(ir['emoji'] ?? '🌸');
        oi.unitPrice = Number(ir['unitPrice'] ?? 0);
        oi.quantity = Number(ir['quantity'] ?? 0);
        oi.subtotal = Number(ir['subtotal'] ?? 0);
        order.items.push(oi);
      }
    }

    // 解析地址
    const addr = r['addressSnapshot'] as Record<string, Object>;
    if (addr) {
      const sa = new ShippingAddress();
      sa.addressId = String(addr['addressId'] ?? '');
      sa.receiverName = String(addr['receiverName'] ?? '');
      sa.phone = String(addr['phone'] ?? '');
      sa.province = String(addr['province'] ?? '');
      sa.city = String(addr['city'] ?? '');
      sa.detail = String(addr['detail'] ?? '');
      sa.isDefault = Boolean(addr['isDefault'] ?? false);
      order.addressSnapshot = sa;
    }

    return order;
  }

  private async saveToStorage(): Promise<void> {
    await AppContext.getInstance().putPrefString(KEY_FLOWER_ORDERS, JSON.stringify(this.orders));
  }
}

/** 购物车项 */
export class CartItem {
  cartId: string = '';
  speciesId: number = 0;
  name: string = '';
  emoji: string = '🌸';
  unitPrice: number = 0;
  quantity: number = 1;
  selected: boolean = true;
}

/** 购物车ViewModel */
@Observed
export class CartViewModel {
  items: CartItem[] = [];
  loading: boolean = false;

  async loadCart(): Promise<void> {
    this.loading = true;
    try {
      const raw = await AppContext.getInstance().getPrefString(KEY_CART_ITEMS, '[]');
      const arr = JSON.parse(raw) as object[];
      const list: CartItem[] = [];
      if (Array.isArray(arr)) {
        for (const item of arr) {
          const ci = new CartItem();
          const r = item as Record<string, Object>;
          ci.cartId = String(r['cartId'] ?? '');
          ci.speciesId = Number(r['speciesId'] ?? 0);
          ci.name = String(r['name'] ?? '');
          ci.emoji = String(r['emoji'] ?? '🌸');
          ci.unitPrice = Number(r['unitPrice'] ?? 0);
          ci.quantity = Number(r['quantity'] ?? 1);
          ci.selected = Boolean(r['selected'] ?? true);
          list.push(ci);
        }
      }
      this.items = list;
    }
    finally {
      this.loading = false;
    }
  }

  /** 添加到购物车 */
  async addToCart(item: MarketItem, quantity: number): Promise<void> {
    // 查找是否已存在
    const existIdx = this.items.findIndex((c: CartItem) => c.speciesId === item.speciesId);
    if (existIdx >= 0) {
      this.items[existIdx].quantity += quantity;
    }
    else {
      const ci = new CartItem();
      ci.cartId = `cart_${Date.now()}_${Math.floor(Math.random() * 10000)}`;
      ci.speciesId = item.speciesId;
      ci.name = item.name;
      ci.emoji = item.emoji;
      ci.unitPrice = this.parsePrice(item.priceText);
      ci.quantity = quantity;
      this.items.push(ci);
    }
    await this.saveToStorage();
  }

  /** 更新数量 */
  async updateQuantity(cartId: string, quantity: number): Promise<void> {
    const item = this.items.find((c: CartItem) => c.cartId === cartId);
    if (item) {
      if (quantity <= 0) {
        await this.removeItem(cartId);
      }
      else {
        item.quantity = quantity;
        await this.saveToStorage();
      }
    }
  }

  /** 移除购物车项 */
  async removeItem(cartId: string): Promise<void> {
    this.items = this.items.filter((c: CartItem) => c.cartId !== cartId);
    await this.saveToStorage();
  }

  /** 清空选中项（下单后）*/
  async clearSelected(): Promise<void> {
    this.items = this.items.filter((c: CartItem) => !c.selected);
    await this.saveToStorage();
  }

  /** 全选/取消全选 */
  async toggleSelectAll(): Promise<void> {
    const allSelected = this.items.every((c: CartItem) => c.selected);
    for (const item of this.items) {
      item.selected = !allSelected;
    }
    await this.saveToStorage();
  }

  /** 获取选中项 */
  getSelectedItems(): CartItem[] {
    return this.items.filter((c: CartItem) => c.selected);
  }

  /** 计算选中总价 */
  getSelectedTotal(): number {
    let total = 0;
    for (const item of this.getSelectedItems()) {
      total += item.unitPrice * item.quantity;
    }
    return Math.round(total * 100) / 100;
  }

  /** 获取商品总数 */
  get totalCount(): number {
    let count = 0;
    for (const item of this.items) {
      count += item.quantity;
    }
    return count;
  }

  private parsePrice(priceText: string): number {
    if (typeof priceText !== 'string' || priceText.length === 0) {
      return 0;
    }
    const raw = Number(priceText.replace('¥', ''));
    return Number.isNaN(raw) ? 0 : raw;
  }

  private async saveToStorage(): Promise<void> {
    await AppContext.getInstance().putPrefString(KEY_CART_ITEMS, JSON.stringify(this.items));
  }
}

/** 安全解析 "¥xx.xx" 价格文本为数字（priceText 可能为 undefined/非字符串，直接 .replace 会抛 TypeError 导致闪退）*/
function safeParsePriceText(priceText: string | undefined): number {
  if (typeof priceText !== 'string' || priceText.length === 0) {
    return 0;
  }
  const raw = Number(priceText.replace('¥', ''));
  return Number.isNaN(raw) ? 0 : raw;
}

/** 将MarketItem转换为OrderItem */
export function marketItemToOrderItem(item: MarketItem, quantity: number): OrderItem {
  const oi = new OrderItem();
  if (!item) {
    return oi;
  }
  oi.speciesId = typeof item.speciesId === 'number' ? item.speciesId : 0;
  oi.name = typeof item.name === 'string' && item.name.length > 0 ? item.name : '花卉商品';
  oi.emoji = typeof item.emoji === 'string' && item.emoji.length > 0 ? item.emoji : '🌸';
  oi.unitPrice = safeParsePriceText(item.priceText);
  const qty = typeof quantity === 'number' && quantity > 0 ? quantity : 1;
  oi.quantity = qty;
  oi.subtotal = Math.round(oi.unitPrice * qty * 100) / 100;
  return oi;
}

/** 将CartItem转换为OrderItem */
export function cartItemToOrderItem(ci: CartItem): OrderItem {
  const oi = new OrderItem();
  if (!ci) {
    return oi;
  }
  oi.speciesId = typeof ci.speciesId === 'number' ? ci.speciesId : 0;
  oi.name = typeof ci.name === 'string' && ci.name.length > 0 ? ci.name : '花卉商品';
  oi.emoji = typeof ci.emoji === 'string' && ci.emoji.length > 0 ? ci.emoji : '🌸';
  oi.unitPrice = typeof ci.unitPrice === 'number' ? ci.unitPrice : 0;
  oi.quantity = typeof ci.quantity === 'number' && ci.quantity > 0 ? ci.quantity : 1;
  oi.subtotal = Math.round(oi.unitPrice * oi.quantity * 100) / 100;
  return oi;
}

/** 订单状态文本映射 */
export const ORDER_STATUS_TEXT: Record<OrderStatus, string> = {
  [OrderStatus.PENDING_PAYMENT]: '待支付',
  [OrderStatus.PAID]: '已支付',
  [OrderStatus.PROCESSING]: '处理中',
  [OrderStatus.SHIPPED]: '已发货',
  [OrderStatus.COMPLETED]: '已完成',
  [OrderStatus.CANCELLED]: '已取消'
};

/** 订单状态颜色映射 */
export const ORDER_STATUS_COLOR: Record<OrderStatus, string> = {
  [OrderStatus.PENDING_PAYMENT]: '#f59e0b',
  [OrderStatus.PAID]: '#3b82f6',
  [OrderStatus.PROCESSING]: '#8b5cf6',
  [OrderStatus.SHIPPED]: '#06b6d4',
  [OrderStatus.COMPLETED]: '#10b981',
  [OrderStatus.CANCELLED]: '#6b7280'
};

/** 格式化ISO时间为可读字符串 */
export function formatOrderTime(iso: string): string {
  if (!iso) {
    return '--';
  }
  const t = Date.parse(iso);
  if (Number.isNaN(t)) {
    return '--';
  }
  const d = new Date(t);
  const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}