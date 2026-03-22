import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  UncontrolledCollapse,
} from "reactstrap";
import classnames from "classnames";
import { IconCode } from "@tabler/icons-react";

type TabItem = {
  id: string;
  title: string;
  content: string[];
};

const tabs: TabItem[] = [
  {
    id: "html",
    title: "HTML",
    content: [
      "Hypertext Markup Language is the standard markup language for documents designed to be displayed in a web browser.",
      "It can be assisted by technologies such as Cascading Style Sheets (CSS) and scripting languages such as JavaScript.",
    ],
  },
  {
    id: "css",
    title: "CSS",
    content: [
      "Cascading Style Sheets (CSS) is a style sheet language used for describing the presentation of a document written in a markup language like HTML.",
      "CSS is a cornerstone technology of the World Wide Web, alongside HTML and JavaScript.",
    ],
  },
  {
    id: "php",
    title: "PHP",
    content: [
      "PHP is a popular general-purpose scripting language that is especially suited to web development.",
      "It was originally created by Rasmus Lerdorf in 1994; the PHP reference implementation is now produced by The PHP Group.",
    ],
  },
];

export const BasicTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(tabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card className="equal-card">
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Basic Tabs</h5>
          <a href="#" id="togglerBasicTabBtn">
            <IconCode data-source="av2" className="source" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Nav tabs className="app-tabs-primary" id="Basic">
            {tabs.map((tab) => (
              <NavItem key={tab.id}>
                <NavLink
                  className={classnames({ active: activeTab === tab.id })}
                  onClick={() => setActiveTab(tab.id)}
                  role="tab"
                  id={`${tab.id}-tab`}
                >
                  {tab.title}
                </NavLink>
              </NavItem>
            ))}
          </Nav>

          <TabContent activeTab={activeTab} className="mt-3" id="BasicContent">
            {tabs.map((tab) => (
              <TabPane
                key={tab.id}
                tabId={tab.id}
                role="tabpanel"
                id={`${tab.id}-tab-pane`}
              >
                {tab.content.map((paragraph, idx) => (
                  <p key={idx}>{paragraph}</p>
                ))}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerBasicTabBtn">
          <pre>
            <code className="language-html">{`<Card>
  <CardHeader>
    <h5>Basic Tabs</h5>
  </CardHeader>
  <CardBody>
    <Nav tabs className="app-tabs-primary" id="Basic">
${tabs
  .map(
    (tab, index) => `      <NavItem>
        <NavLink
          ${index === 0 ? 'className="active"' : ""}
          id="${tab.id}-tab"
          role="tab"
        >
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent activeTab="html" className="mt-3" id="BasicContent">
${tabs
  .map(
    (tab) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
      >
${tab.content.map((text) => `        <p>${text}</p>`).join("\n")}
      </TabPane>`
  )
  .join("\n")}
    </TabContent>
  </CardBody>
</Card>`}</code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};
