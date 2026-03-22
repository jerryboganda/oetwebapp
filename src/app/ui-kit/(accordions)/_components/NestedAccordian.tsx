import React, { useState } from "react";
import {
  Col,
  Card,
  CardHeader,
  CardBody,
  Collapse,
  Button,
  Row,
  UncontrolledCollapse,
} from "reactstrap";
import {
  IconCode,
  IconMessageReport,
  IconTicket,
  IconTransform,
} from "@tabler/icons-react";

type AccordionItem = {
  id: string;
  icon?: JSX.Element;
  title: string;
  content: string;
};

type NestedAccordionItem = {
  id: string;
  title: string;
  content: string;
};

type NestedAccordionGroup = {
  id: string;
  title: string;
  children: NestedAccordionItem[];
};

const noIconItems: AccordionItem[] = [
  {
    id: "noicon1",
    icon: <IconMessageReport size={20} className="me-1 f-s-20" />,
    title: "What happens if I just pay my ticket?",
    content:
      "Paying your ticket without contesting it can result in a conviction on your driving record. This can have long-lasting consequences such as increased insurance rates, driver's license suspension, employment restrictions, and additional fines imposed by your state's DMV.",
  },
  {
    id: "noicon2",
    icon: <IconTicket size={20} className="me-1 f-s-20" />,
    title: "Can this ticket affect my job?",
    content:
      "Absolutely, tickets recorded on your driving history can result in disqualification from driving commercially or obtaining a commercial driver's license (CDL). They can also disqualify you from driving for ride-sharing services such as Uber or Lyft, which require a clean driving record.",
  },
  {
    id: "noicon3",
    icon: <IconTransform size={20} className="me-1 f-s-20" />,
    title: "How long does it take to resolve my case?",
    content:
      "It really depends on the court your ticket landed in. Some courts move faster than others, but on average, it could take about 1-3 months. This is perfectly normal in the legal process.",
  },
];

const multiLevelItems: NestedAccordionGroup[] = [
  {
    id: "level1-1",
    title: "This is level one accordion #1",
    children: [
      {
        id: "level2-1-1",
        title: "This is level two accordion #1",
        content:
          "This is the first item's accordion body. It is shown by default.",
      },
      {
        id: "level2-1-2",
        title: "This is level two accordion #2",
        content:
          "This is the second item's accordion body. It is hidden by default, until the collapse plugin adds the appropriate classes that we use to style each element. These classes control the overall appearance, as well as the showing and hiding via CSS transitions.",
      },
    ],
  },
  {
    id: "level1-2",
    title: "This is level one accordion #2",
    children: [],
  },
];

const AccordionSections: React.FC = () => {
  const [openNoIcon, setOpenNoIcon] = useState<string | null>("noicon1");
  const [openMultiLevel, setOpenMultiLevel] = useState<string | null>(
    "level1-1"
  );
  const [openNested, setOpenNested] = useState<string | null>(null);

  return (
    <Row>
      {/* No Icon Accordion */}
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5>No Icon Indicators</h5>
            <a href="#" id="togglerAccordions">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="accordion app-accordion accordion-light-danger app-accordion-no-icon">
              {noIconItems.map(({ id, icon, title, content }) => (
                <div className="accordion-item" key={id}>
                  <h2 className="accordion-header">
                    <Button
                      color="link"
                      className={`accordion-button ${openNoIcon !== id ? "collapsed" : ""}`}
                      onClick={() =>
                        setOpenNoIcon(openNoIcon === id ? null : id)
                      }
                    >
                      {icon}
                      {title}
                    </Button>
                  </h2>
                  <Collapse isOpen={openNoIcon === id}>
                    <div className="accordion-body">{content}</div>
                  </Collapse>
                </div>
              ))}
            </div>
            <UncontrolledCollapse toggler="#togglerAccordions">
              <pre>
                <code className="language-html">
                  {`<Accordion className="app-accordion accordion-light-danger app-accordion-no-icon" id="accordionnoiconExample">
${noIconItems
  .map(
    (item, index) => `  <AccordionItem key="${index}">
    <AccordionHeader targetId="${index + 1}">
      <button className="accordion-button${index !== 0 ? " collapsed" : ""}" type="button" aria-expanded="${index === 0}" aria-controls="noicon-collapse-${index + 1}">
        ${item.icon?.props?.children ? "" : ""}${item.title}
      </button>
    </AccordionHeader>
    <AccordionBody accordionId="${index + 1}">
      ${item.content}
    </AccordionBody>
  </AccordionItem>`
  )
  .join("\n")}
</Accordion>`}
                </code>
              </pre>
            </UncontrolledCollapse>
          </CardBody>
        </Card>
      </Col>

      {/* Multi-Level Accordion */}
      <Col lg={6}>
        <Card className="equal-card">
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5>Multi Level Indicators</h5>
            <a href="#" id="togglerMulti">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="accordion app-accordion accordion-light-success app-accordion-plus">
              {multiLevelItems.map((group) => (
                <div className="accordion-item" key={group.id}>
                  <h2 className="accordion-header">
                    <Button
                      color="link"
                      className={`accordion-button ${openMultiLevel !== group.id ? "collapsed" : ""}`}
                      onClick={() =>
                        setOpenMultiLevel(
                          openMultiLevel === group.id ? null : group.id
                        )
                      }
                    >
                      {group.title}
                    </Button>
                  </h2>
                  <Collapse isOpen={openMultiLevel === group.id}>
                    <div className="accordion-body">
                      {group.children.length > 0 && (
                        <div className="accordion mt-2 app-accordion app-accordion-icon-left app-accordion-plus">
                          {group.children.map((child) => (
                            <div className="accordion-item" key={child.id}>
                              <h2 className="accordion-header">
                                <Button
                                  color="link"
                                  className={`accordion-button ${openNested !== child.id ? "collapsed" : ""}`}
                                  onClick={() =>
                                    setOpenNested(
                                      openNested === child.id ? null : child.id
                                    )
                                  }
                                >
                                  {child.title}
                                </Button>
                              </h2>
                              <Collapse isOpen={openNested === child.id}>
                                <div className="accordion-body">
                                  {child.content}
                                </div>
                              </Collapse>
                            </div>
                          ))}
                        </div>
                      )}
                      {group.children.length === 0 && (
                        <p>
                          This is the second item&#39;s accordion body. It is
                          hidden by default.
                        </p>
                      )}
                    </div>
                  </Collapse>
                </div>
              ))}
            </div>
            <UncontrolledCollapse toggler="#togglerMulti">
              <pre>
                <code className="language-html">
                  {`
<Accordion flush className="app-accordion accordion-light-success app-accordion-plus" id="nestingExample">
${multiLevelItems
  .map((group, groupIndex) => {
    const parentId = `nestingcollapse${groupIndex + 1}`;

    const nestedHTML =
      group.children && group.children.length > 0
        ? `
    <Accordion flush className="app-accordion app-accordion-icon-left app-accordion-plus" id="nestingtwoExample${groupIndex}">
${group.children
  .map((child, childIndex) => {
    const childId = `nestingtwocollapse${groupIndex}${childIndex + 1}`;
    return `      <AccordionItem>
        <AccordionHeader targetId="${childId}">
          ${child.title}
        </AccordionHeader>
        <AccordionBody accordionId="${childId}">
          ${child.content}
        </AccordionBody>
      </AccordionItem>`;
  })
  .join("\n")}
    </Accordion>`
        : `<p>This is the second item's accordion body. It is hidden by default.</p>`;

    return `  <AccordionItem>
    <AccordionHeader targetId="${parentId}">
      ${group.title}
    </AccordionHeader>
    <AccordionBody accordionId="${parentId}">
      ${nestedHTML}
    </AccordionBody>
  </AccordionItem>`;
  })
  .join("\n")}
</Accordion>`}
                </code>
              </pre>
            </UncontrolledCollapse>
          </CardBody>
        </Card>
      </Col>
    </Row>
  );
};

export default AccordionSections;
