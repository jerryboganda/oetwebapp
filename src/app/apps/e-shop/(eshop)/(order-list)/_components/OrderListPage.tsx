"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Table,
  Badge,
  Button,
  Card,
  CardBody,
  Container,
  Row,
  FormGroup,
  Label,
  Form,
  ModalBody,
  ModalHeader,
  Modal,
  InputGroupText,
  InputGroup,
  ModalFooter,
  Input,
  Col,
} from "reactstrap";
import Link from "next/link";
import { orders } from "@/Data/Apps/Orderpage/Orderpage";
import { IconEdit, IconEye, IconStack2, IconTrash } from "@tabler/icons-react";

const OrderListPage = () => {
  const [modal, setModal] = useState(false);

  const toggle = () => setModal(!modal);

  const [ordersListPage, setOrdersListPage] = useState(orders);

  const handleDelete = (orderId: number) => {
    const updatedOrders = orders.filter((order) => order.idNum !== orderId);
    setOrdersListPage(updatedOrders);
  };
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Orders List"
          title="Apps"
          path={["E-shop", "Orders List"]}
          Icon={IconStack2}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardBody className="p-0">
                <div className="order-list-datatable app-scroll table-responsive">
                  <Table className="table-bottom-border align-middle mb-0">
                    <thead>
                      <tr>
                        <th>
                          <input
                            type="checkbox"
                            id="select-all"
                            className="form-check-input"
                          />
                        </th>
                        <th>Order Id</th>
                        <th>Customer</th>
                        <th>Product</th>
                        <th>Status</th>
                        <th>Order Date</th>
                        <th>Payment Method</th>
                        <th>Amount</th>
                        <th>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {ordersListPage.map((order, index) => (
                        <tr key={index}>
                          <td>
                            <input
                              type="checkbox"
                              className="form-check-input"
                            />
                          </td>
                          <td>{order.id}</td>
                          <td className="d-flex align-items-center gap-2">
                            <div
                              className={`h-25 w-25 d-flex-center b-r-50 overflow-hidden text-bg-secondary`}
                            >
                              <img
                                src={order.customer.avatar}
                                alt={order.customer.avatar}
                                className="img-fluid"
                              />
                            </div>
                            <span className="title-text mb-0">
                              {order.customer.name}
                            </span>
                          </td>
                          <td>{order.product}</td>
                          <td>
                            <Badge
                              color={(() => {
                                switch (order.status) {
                                  case "CANCELLED":
                                    return "light-danger";
                                  case "DELIVERED":
                                    return "light-success";
                                  case "INPROGRESS":
                                    return "light-warning";
                                  case "PICKUPS":
                                    return "light-info";
                                  case "RETURNS":
                                    return "light-secondary";
                                  default:
                                    return "light-light";
                                }
                              })()}
                            >
                              {order.status}
                            </Badge>
                          </td>
                          <td>{order.orderDate}</td>
                          <td>{order.paymentMethod}</td>
                          <td>{order.amount}</td>
                          <td>
                            <Link
                              href="/apps/e-shop/orders-details"
                              className="btn btn-outline-primary icon-btn w-30 h-30 b-r-22 me-2"
                            >
                              <IconEye size={18} />
                            </Link>
                            <Button
                              color="outline-success"
                              className="icon-btn w-30 h-30 b-r-22 me-2"
                              onClick={toggle}
                            >
                              <IconEdit size={18} />
                            </Button>
                            <Button
                              color="outline-danger"
                              className="icon-btn w-30 h-30 b-r-22 delete-btn"
                              onClick={() => handleDelete(order.idNum)}
                            >
                              <IconTrash size={18} />
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
        <Modal
          isOpen={modal}
          toggle={toggle}
          backdrop="static"
          keyboard={false}
        >
          <ModalHeader toggle={toggle}>Edit Order</ModalHeader>
          <ModalBody>
            <Form className="app-form">
              <FormGroup>
                <Label for="Customer">Customer</Label>
                <Input
                  type="text"
                  id="Customer"
                  placeholder="Bette Hagenes"
                  defaultValue="Bette Hagenes"
                />
              </FormGroup>

              <FormGroup>
                <Label for="Product">Product</Label>
                <Input
                  type="text"
                  id="Product"
                  placeholder="jacket"
                  defaultValue="jacket"
                />
              </FormGroup>

              <FormGroup>
                <Label for="OrderDate">Order Date</Label>
                <Input type="datetime-local" id="OrderDate" />
              </FormGroup>

              <FormGroup>
                <Label for="Payment">Payment Method</Label>
                <Input type="select" id="Payment">
                  <option value="">Select Your Payment Method</option>
                  <option value="1">Mastercard</option>
                  <option value="2">Visa</option>
                  <option value="3">Paypal</option>
                </Input>
              </FormGroup>

              <FormGroup>
                <Label for="Amount">Amount</Label>
                <InputGroup>
                  <InputGroupText>$</InputGroupText>
                  <Input
                    type="text"
                    id="Amount"
                    placeholder="Enter Amount"
                    defaultValue="500"
                  />
                </InputGroup>
              </FormGroup>
            </Form>
          </ModalBody>

          <ModalFooter>
            <Button color="secondary" onClick={toggle}>
              Close
            </Button>
            <Button color="primary">Edit</Button>
          </ModalFooter>
        </Modal>
      </Container>
    </div>
  );
};

export default OrderListPage;
