import { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  Row,
  Col,
} from "reactstrap";
import PaymentDataForm from "./PaymentDataForm";

const PaymentForm = () => {
  const [activeTab, setActiveTab] = useState("1");

  const toggleTab = (tab: string) => {
    if (activeTab !== tab) {
      setActiveTab(tab);
    }
  };

  return (
    <div>
      <Card>
        <CardHeader>
          <h5>Payment Information Form</h5>
        </CardHeader>
        <CardBody>
          <Row>
            <Col md="4" xxl="3">
              <Nav pills vertical className="step-status">
                <NavItem>
                  <NavLink
                    className={`btn flex-grow-1 text-start ${activeTab === "1" ? "active" : ""}`}
                    onClick={() => toggleTab("1")}
                  >
                    <img
                      src="/images/checkbox-radio/logo1.png"
                      className="w-35 h-35"
                      alt=""
                    />
                    <span>Credit / Debit Card</span>
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink
                    className={`btn flex-grow-1 text-start ${activeTab === "2" ? "active" : ""}`}
                    onClick={() => toggleTab("2")}
                  >
                    <img
                      src="/images/checkbox-radio/logo2.png"
                      className="w-35 h-35"
                      alt=""
                    />
                    <span>Visa Card</span>
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink
                    className={`btn flex-grow-1 text-start ${activeTab === "3" ? "active" : ""}`}
                    onClick={() => toggleTab("3")}
                  >
                    <img
                      src="/images/checkbox-radio/logo3.png"
                      className="w-35 h-35"
                      alt=""
                    />
                    <span>Paypal</span>
                  </NavLink>
                </NavItem>
              </Nav>
            </Col>
            <Col md="8" xxl="9">
              <TabContent activeTab={activeTab} className="mt-3">
                <TabPane tabId="1">
                  <PaymentDataForm />
                </TabPane>
                <TabPane tabId="2">
                  <PaymentDataForm />
                </TabPane>
                <TabPane tabId="3">
                  <PaymentDataForm />
                </TabPane>
              </TabContent>
            </Col>
          </Row>
        </CardBody>
      </Card>
    </div>
  );
};

export default PaymentForm;
