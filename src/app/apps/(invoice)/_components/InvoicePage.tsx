"use client";
import React from "react";
import { Button, Card, CardBody, Col, Container, Row, Table } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconArrowBarToDown,
  IconPrinter,
  IconSend,
  IconStack2,
} from "@tabler/icons-react";

const InvoicePage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Invoice"
          title="Apps"
          path={["Invoice"]}
          Icon={IconStack2}
        />
        <div className="container-lg">
          <Row>
            <Col xs={12}>
              <Card>
                <CardBody>
                  <div className="table-responsive app-scroll">
                    <Table className="mb-0">
                      <tbody>
                        <tr>
                          <td>
                            <table className="table table-lg w-100 invoice-header mb-0">
                              <tbody>
                                <tr>
                                  <td>
                                    <div className=" mb-3">
                                      <div className="mb-3">
                                        <img
                                          src="/images/logo/polytronx-dark.svg"
                                          className="w-200"
                                          alt=""
                                        />
                                      </div>
                                      <div>
                                        <address>
                                          35 O&#39;Connor, Ottawa Ontario <br />
                                          K1P 5M4 ,Canada <br />
                                          support@polytronx.com
                                        </address>
                                        (613) 233-1222
                                      </div>
                                    </div>
                                  </td>
                                  <td>
                                    <div className="text-end">
                                      <div className="mb-1">
                                        <h3 className="text-primary">
                                          INVOICE
                                        </h3>
                                      </div>
                                      <p>
                                        Invoice <strong>#800123</strong>
                                      </p>
                                      <p>
                                        Invoice Date{" "}
                                        <strong>Apr 18,2024</strong>
                                      </p>
                                      <p>
                                        Invoice Due{" "}
                                        <strong>July 18,2024</strong>
                                      </p>
                                    </div>
                                  </td>
                                </tr>
                              </tbody>
                            </table>
                          </td>
                        </tr>
                      </tbody>
                      <tbody>
                        <tr>
                          <td>
                            <table className="invoice-details-table table table-lg w-100">
                              <tbody>
                                <tr>
                                  <td>
                                    <div>
                                      <h6 className="f-w-600">Bill From</h6>
                                      Mark McKenzie
                                      <address>
                                        35 O&#39;Connor, Ottawa Ontario <br />
                                        K1P 5M4 ,Canada <br />
                                        support@polytronx.com
                                      </address>
                                      (613) 233-1222
                                    </div>
                                  </td>
                                  <td>
                                    <div>
                                      <h6 className="f-w-600">Bill To</h6>
                                      Fleta Walsh
                                      <address>
                                        255 E Flamingo Rd, Las Vegas,
                                        <br /> North Carolina, US-89169
                                        <br />
                                        billing@polytronx.com
                                      </address>
                                      (702) 893-8933
                                    </div>
                                  </td>
                                  <td>
                                    <div className=" text-end">
                                      <h6 className="f-w-600">
                                        Payment Method
                                      </h6>
                                      <p>Bank : Bank of Us</p>
                                      <p>Card Holder : Fleta Walsh</p>
                                      <p>Card Number : xxx xxxx xxxx 8652</p>
                                      <p>Payment Method: Debit card </p>
                                    </div>
                                  </td>
                                </tr>
                              </tbody>
                            </table>
                          </td>
                        </tr>
                      </tbody>

                      <tbody>
                        <tr>
                          <td>
                            <table className="table table-lg table-bottom-border invoice-table w-100 mb-0">
                              <thead>
                                <tr>
                                  <th className="f-w-500" scope="col">
                                    No
                                  </th>
                                  <th className="f-w-500" scope="col">
                                    Item
                                  </th>
                                  <th className="f-w-500" scope="col">
                                    Description
                                  </th>
                                  <th className="f-w-500" scope="col">
                                    Quantity
                                  </th>
                                  <th className="f-w-500" scope="col">
                                    Unit Cost
                                  </th>
                                  <th className="f-w-500" scope="col">
                                    Total
                                  </th>
                                </tr>
                              </thead>
                              <tbody>
                                <tr>
                                  <td>1</td>
                                  <td className="f-w-500">
                                    <h6>H & M Round Neck</h6>
                                  </td>
                                  <td className="f-s-14 text-secondary">
                                    <p>
                                      Men&#39;s Graphic Tees & Printed T-shirts
                                    </p>
                                  </td>
                                  <td>2</td>
                                  <td>$400</td>
                                  <td>$200</td>
                                </tr>
                                <tr>
                                  <td>2</td>
                                  <td className="f-w-500">
                                    <h6>Mast & Harbour</h6>
                                  </td>
                                  <td className="f-s-14 text-secondary">
                                    <p>
                                      Men Full Sleeve Solid Hooded Sweatshirt !
                                    </p>
                                  </td>
                                  <td>1</td>
                                  <td>$600</td>
                                  <td>$400</td>
                                </tr>
                                <tr>
                                  <td>3</td>
                                  <td className="f-w-500">
                                    <h6>Adidas</h6>
                                  </td>
                                  <td className="f-s-14 text-secondary">
                                    <p>
                                      Jauntza M Running Shoes For Men (Blue)
                                    </p>
                                  </td>
                                  <td>5</td>
                                  <td>$500</td>
                                  <td>$250</td>
                                </tr>
                                <tr>
                                  <td>4</td>
                                  <td className="f-w-500">
                                    <h6>Stylist Jackets</h6>
                                  </td>
                                  <td className="f-s-14 text-secondary">
                                    <p>Sky modern jacket for kid</p>
                                  </td>
                                  <td>1</td>
                                  <td>$200</td>
                                  <td>$150</td>
                                </tr>
                                <tr>
                                  <td colSpan={3}></td>
                                  <td colSpan={3}>
                                    <table className="table mb-0">
                                      <tbody>
                                        <tr>
                                          <td>
                                            <h6>Sub Total</h6>
                                          </td>
                                          <td className="text-end">$2,150</td>
                                        </tr>
                                        <tr>
                                          <td>
                                            <h6>Tax</h6>
                                          </td>
                                          <td className="text-end">$100</td>
                                        </tr>
                                        <tr>
                                          <td>
                                            <h6>Shipping Charge</h6>
                                          </td>
                                          <td className="text-end">$500</td>
                                        </tr>

                                        <tr>
                                          <th className="border-0 pb-0">
                                            Total
                                          </th>
                                          <th className="border-0 text-end pb-0">
                                            $2,750
                                          </th>
                                        </tr>
                                      </tbody>
                                    </table>
                                  </td>
                                </tr>
                              </tbody>
                            </table>
                          </td>
                        </tr>
                      </tbody>
                    </Table>
                  </div>
                </CardBody>
              </Card>

              <div className="invoice-footer float-end mb-3">
                <Button
                  type="button"
                  color="primary"
                  className="m-1 rounded"
                  onClick={() => window.print()}
                >
                  <IconPrinter size={15} /> Print
                </Button>
                <Button type="button" color="secondary" className="m-1 rounded">
                  <IconSend size={15} /> Send Invoice
                </Button>
                <Button type="button" color="success" className="m-1 rounded">
                  <IconArrowBarToDown size={15} /> Download
                </Button>
              </div>
            </Col>
          </Row>
        </div>
      </Container>
    </div>
  );
};

export default InvoicePage;
